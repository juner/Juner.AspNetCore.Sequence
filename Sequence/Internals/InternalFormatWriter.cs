using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;


#if NET9_0_OR_GREATER
using System.IO.Pipelines;
#endif

namespace Juner.AspNetCore.Sequence.Internals;

internal class InternalFormatWriter(object? Object, Type ObjectType, JsonSerializerOptions serializerOptions, Encoding selectedEncoding, HttpContext httpContext, EnumerableType OutputType, Type type, ILogger logger, ReadOnlyMemory<byte> begin = default, ReadOnlyMemory<byte> end = default)
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="serializerOptions"></param>
    /// <param name="context"></param>
    /// <param name="selectedEncoding"></param>
    /// <param name="begin"></param>
    /// <param name="end"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static InternalFormatWriter Create(JsonSerializerOptions serializerOptions, OutputFormatterWriteContext context, Encoding selectedEncoding, ILogger logger, ReadOnlyMemory<byte> begin = default, ReadOnlyMemory<byte> end = default)
    {
        if (!TryGetOutputMode(context.ObjectType, out var outputType, out var type))
            throw new InvalidOperationException();
        return new InternalFormatWriter(context.Object, context.ObjectType, serializerOptions, selectedEncoding, context.HttpContext, outputType, type, logger, begin, end);
    }
    readonly ReadOnlyMemory<byte> Begin = begin;
    readonly ReadOnlyMemory<byte> End = end;
    readonly JsonSerializerOptions SerializerOptions = serializerOptions;
    readonly JsonTypeInfo? JsonTypeInfo = GetJsonTypeInfo(serializerOptions, type);
    readonly Encoding SelectedEncoding = selectedEncoding;
    readonly HttpContext httpContext = httpContext;
    readonly EnumerableType OutputType = OutputType;
    private readonly ILogger logger = logger;
    static IDictionary<Type, EnumerableType>? _targetInterface;
    static IDictionary<Type, EnumerableType> TargetInterfaces => _targetInterface ??= new Dictionary<Type, EnumerableType>()
    {
        {typeof(IAsyncEnumerable<>), EnumerableType.AsyncEnumerable},
        {typeof(IEnumerable<>), EnumerableType.Enumerable },
        {typeof(ChannelReader<>), EnumerableType.ChannelReader },
    }.AsReadOnly();
    public static bool TryGetOutputMode([NotNullWhen(true)] Type? objectType, [NotNullWhen(true)] out EnumerableType outputType, [NotNullWhen(true)] out Type type)
    {
        outputType = default;
        type = default!;
        // 型なしは無視する
        if (objectType is null) return false;
        // 文字列は除外
        if (objectType == typeof(string)) return false;
        var interfaces = objectType switch
        {
            { IsInterface: true } => [objectType, .. objectType.GetInterfaces()],
            _ => objectType.GetInterfaces().Where(v => v.IsGenericType),
        };
        var find = false;
        foreach (var i in interfaces)
        {
            find = TargetInterfaces.TryGetValue(i.GetGenericTypeDefinition(), out outputType);
            if (find)
            {
                type = i.GetGenericArguments()[0];
                break;
            }
        }
        return find;
    }

    static JsonTypeInfo? GetJsonTypeInfo(JsonSerializerOptions serializerOptions, Type type)
    {
        var declaredTypeJsonInfo = serializerOptions.GetTypeInfo(type);

        var runtimeType = type;
        if (declaredTypeJsonInfo.ShouldUseWith(runtimeType))
            return declaredTypeJsonInfo;
        else
            return null;
    }
    public Task WriteResponseBodyAsync(CancellationToken cancellationToken)
        => WriteAsync(
            ObjectType,
            Object,
            httpContext,
            JsonTypeInfo,
            SerializerOptions,
            SelectedEncoding,
            logger,
            Begin,
            End,
            cancellationToken);

    public static Task WriteAsync<Enumerable, T>(
        Enumerable? @object,
        HttpContext httpContext, 
        JsonTypeInfo? jsonTypeInfo, 
        JsonSerializerOptions serializeOptions,
        Encoding SelectedEncoding, 
        ILogger logger, 
        ReadOnlyMemory<byte> Begin,
        ReadOnlyMemory<byte> End, 
        CancellationToken cancellationToken)
    {
        if (!TryGetOutputMode(typeof(Enumerable), out var OutputType, out var type))
            throw new InvalidOperationException($"not support output type ");
        var jsonTypeInfo2 = (JsonTypeInfo<T>?)jsonTypeInfo;
        if (OutputType is EnumerableType.AsyncEnumerable)
            return WriteAsyncFromAsyncEnumerable(@object as IAsyncEnumerable<T>, httpContext, jsonTypeInfo2, serializeOptions, SelectedEncoding, logger, Begin, End, cancellationToken);
        else if (OutputType is EnumerableType.Enumerable or EnumerableType.Array or EnumerableType.List)
            return WriteAsyncFromEnumerable(@object as IEnumerable<T>, httpContext, jsonTypeInfo2, serializeOptions, SelectedEncoding, logger, Begin, End, cancellationToken);
        else if (OutputType is EnumerableType.ChannelReader)
            return WriteAsyncFromChannelReader(@object as ChannelReader<T>, httpContext, jsonTypeInfo2, serializeOptions, SelectedEncoding, logger, Begin, End, cancellationToken);
        return Task.FromException(new NotImplementedException($"not support pattern {@object?.GetType().Name ?? "null"} and {OutputType}"));
    }

    static readonly ConcurrentDictionary<Type, Delegate> cache = new();

    public static Task WriteAsync(
        Type objectType,
        object? @object,
        HttpContext httpContext,
        JsonTypeInfo? jsonTypeInfo,
        JsonSerializerOptions serializeOptions,
        Encoding selectedEncoding,
        ILogger logger,
        ReadOnlyMemory<byte> begin,
        ReadOnlyMemory<byte> end,
        CancellationToken cancellationToken)
    {
        var del = cache.GetOrAdd(objectType, CreateDelegate);

        var func =
            (Func<
                object?,
                HttpContext,
                JsonTypeInfo?,
                JsonSerializerOptions,
                Encoding,
                ILogger,
                ReadOnlyMemory<byte>,
                ReadOnlyMemory<byte>,
                CancellationToken,
                Task>)del;

        return func(
            @object,
            httpContext,
            jsonTypeInfo,
            serializeOptions,
            selectedEncoding,
            logger,
            begin,
            end,
            cancellationToken);
    }

    static Delegate CreateDelegate(Type objectType)
    {
        if (!TryGetOutputMode(objectType, out _, out var type))
            throw new InvalidOperationException($"{objectType} not found elementType");
        var method =
            typeof(InternalFormatWriter)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(static m =>
                m.Name == nameof(WriteAsync) &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 9)
            .MakeGenericMethod(objectType, type);

        // parameters
        var pObj = Expression.Parameter(typeof(object), "object");
        var pHttp = Expression.Parameter(typeof(HttpContext), "httpContext");
        var pJsonTypeInfo = Expression.Parameter(typeof(JsonTypeInfo), "jsonTypeInfo");
        var pOptions = Expression.Parameter(typeof(JsonSerializerOptions), "serializeOptions");
        var pEncoding = Expression.Parameter(typeof(Encoding), "selectedEncoding");
        var pLogger = Expression.Parameter(typeof(ILogger), "logger");
        var pBegin = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "begin");
        var pEnd = Expression.Parameter(typeof(ReadOnlyMemory<byte>), "end");
        var pCancel = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        // object → T
        var castObj = Expression.Convert(pObj, objectType);

        // JsonTypeInfo → JsonTypeInfo<T>
        var jsonTypeInfoT = typeof(JsonTypeInfo<>).MakeGenericType(type);
        var castJsonTypeInfo = Expression.Convert(pJsonTypeInfo, jsonTypeInfoT);

        var call = Expression.Call(
            method,
            castObj,
            pHttp,
            castJsonTypeInfo,
            pOptions,
            pEncoding,
            pLogger,
            pBegin,
            pEnd,
            pCancel);

        var lambda =
            Expression.Lambda<
                Func<
                    object?,
                    HttpContext,
                    JsonTypeInfo?,
                    JsonSerializerOptions,
                    Encoding,
                    ILogger,
                    ReadOnlyMemory<byte>,
                    ReadOnlyMemory<byte>,
                    CancellationToken,
                    Task>>
            (
                call,
                pObj,
                pHttp,
                pJsonTypeInfo,
                pOptions,
                pEncoding,
                pLogger,
                pBegin,
                pEnd,
                pCancel
            );

        return lambda.Compile();
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed ASP.NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    public static async Task WriteAsyncFromEnumerable<Enumerable, T>(Enumerable? values, HttpContext httpContext, JsonTypeInfo<T>? JsonTypeInfo, JsonSerializerOptions SerializerOptions, Encoding SelectedEncoding, ILogger logger, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken)
  where Enumerable : IEnumerable<T>
    {
        if (values is null) return;
        if (SelectedEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            try
            {
#if NET9_0_OR_GREATER
                {
                    var responseWriter = httpContext.Response.BodyWriter;
                    foreach (var value in values)
                        await WriteRecordAsync(responseWriter, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
                }
#else
                {
                    var stream = httpContext.Response.Body;
                    foreach (var value in values)
                        await WriteRecordAsync(stream, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
                }
#endif
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        else
        {
            var transcodingStream = Encoding.CreateTranscodingStream(httpContext.Response.Body, SelectedEncoding, Encoding.UTF8, leaveOpen: true);

            ExceptionDispatchInfo? exceptionDispatchInfo = null;
            try
            {
                foreach (var value in values)
                    await WriteRecordAsync(transcodingStream, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
            }
            catch (Exception ex)
            {
                exceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    await transcodingStream.DisposeAsync();
                }
                catch when (exceptionDispatchInfo != null)
                {
                }
                exceptionDispatchInfo?.Throw();
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed ASP.NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    public static async Task WriteAsyncFromAsyncEnumerable<AsyncEnumerable, T>(
        AsyncEnumerable? values, HttpContext httpContext, JsonTypeInfo<T>? JsonTypeInfo, JsonSerializerOptions SerializerOptions, Encoding SelectedEncoding, ILogger logger, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken)
      where AsyncEnumerable : IAsyncEnumerable<T>
    {
        if (values is null) return;
        if (SelectedEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            try
            {
#if NET9_0_OR_GREATER
                {
                    var responseWriter = httpContext.Response.BodyWriter;
                    await foreach (var value in values)
                        await WriteRecordAsync(responseWriter, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
                }
#else
                {
                    var stream = httpContext.Response.Body;
                    await foreach (var value in values)
                        await WriteRecordAsync(stream, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
                }
#endif
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        else
        {
            var transcodingStream = Encoding.CreateTranscodingStream(httpContext.Response.Body, SelectedEncoding, Encoding.UTF8, leaveOpen: true);

            ExceptionDispatchInfo? exceptionDispatchInfo = null;
            try
            {
                await foreach (var value in values)
                    await WriteRecordAsync(transcodingStream, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
            }
            catch (Exception ex)
            {
                exceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    await transcodingStream.DisposeAsync();
                }
                catch when (exceptionDispatchInfo != null)
                {
                }
                exceptionDispatchInfo?.Throw();
            }
        }
    }
    public static async Task WriteAsyncFromChannelReader<ChannelReader, T>(
        ChannelReader? values, HttpContext httpContext, JsonTypeInfo<T>? JsonTypeInfo, JsonSerializerOptions SerializerOptions, Encoding SelectedEncoding, ILogger logger, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken)
      where ChannelReader : ChannelReader<T>
    {
        if (values is null) return;
        if (SelectedEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            try
            {
#if NET9_0_OR_GREATER
                {
                    var responseWriter = httpContext.Response.BodyWriter;   
                    while(await values.WaitToReadAsync(cancellationToken))
                    {                  
                        cancellationToken.ThrowIfCancellationRequested();
                        var value = await values.ReadAsync(cancellationToken);
                        await WriteRecordAsync(responseWriter, value, JsonTypeInfo, SerializerOptions, Begin, End, cancellationToken);
                    }
                }
#else
                {
                    var stream = httpContext.Response.Body;
                    while(await values.WaitToReadAsync(cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var value = await values.ReadAsync(cancellationToken);
                        await WriteRecordAsync(stream, value, JsonTypeInfo, SerializerOptions, Begin, End);
                    }
                }
#endif
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        else
        {
            var transcodingStream = Encoding.CreateTranscodingStream(httpContext.Response.Body, SelectedEncoding, Encoding.UTF8, leaveOpen: true);

            ExceptionDispatchInfo? exceptionDispatchInfo = null;
            try
            {
                while (await values.WaitToReadAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = await values.ReadAsync(cancellationToken);
                    await WriteRecordAsync(transcodingStream, value, JsonTypeInfo, SerializerOptions, Begin, End);
                }
            }
            catch (Exception ex)
            {
                exceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    await transcodingStream.DisposeAsync();
                }
                catch when (exceptionDispatchInfo != null)
                {
                }
                exceptionDispatchInfo?.Throw();
            }
        }
    }

#if NET9_0_OR_GREATER
    static async ValueTask WriteRecordAsync<T>(PipeWriter writer, T value, JsonTypeInfo<T>? jsonTypeInfo, JsonSerializerOptions SerializerOptions, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken)
    {
        if (Begin is not { IsEmpty: true})
            await writer.WriteAsync(Begin, cancellationToken);
        if (jsonTypeInfo is not null)
            await JsonSerializer.SerializeAsync(writer, value, jsonTypeInfo, cancellationToken);
        else
            await JsonSerializer.SerializeAsync(writer, value, SerializerOptions, cancellationToken);
        if (End is not { IsEmpty: true})
            await writer.WriteAsync(End, cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
#endif
    static async ValueTask WriteRecordAsync<T>(Stream writer, T value, JsonTypeInfo<T>? jsonTypeInfo, JsonSerializerOptions SerializerOptions, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken = default)
    {
        if (Begin is not { IsEmpty: true })
            await writer.WriteAsync(Begin, cancellationToken);
        if (jsonTypeInfo is not null)
            await JsonSerializer.SerializeAsync(writer, value, jsonTypeInfo, cancellationToken);
        else
            await JsonSerializer.SerializeAsync(writer, value, SerializerOptions, cancellationToken);
        if (End is not { IsEmpty: true })
            await writer.WriteAsync(End, cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}