using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

#if NET9_0_OR_GREATER
using System.IO.Pipelines;
#endif

namespace Juner.AspNetCore.Sequence.Internals;

internal class InternalFormatWriter(object? Object, Type ObjectType, JsonSerializerOptions serializerOptions, Encoding selectedEncoding, HttpContext httpContext, EnumerableType OutputType, Type type, ReadOnlyMemory<byte> begin = default, ReadOnlyMemory<byte> end = default)
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
    public static InternalFormatWriter Create(JsonSerializerOptions serializerOptions, OutputFormatterWriteContext context, Encoding selectedEncoding, ReadOnlyMemory<byte> begin = default, ReadOnlyMemory<byte> end = default)
    {
        if (!TryGetOutputMode(context.ObjectType, out var outputType, out var type))
            throw new InvalidOperationException();
        return new InternalFormatWriter(context.Object, context.ObjectType, serializerOptions, selectedEncoding, context.HttpContext, outputType, type, begin, end);
    }
    readonly ReadOnlyMemory<byte> Begin = begin;
    readonly ReadOnlyMemory<byte> End = end;
    readonly JsonSerializerOptions SerializerOptions = serializerOptions;
    readonly JsonTypeInfo? JsonTypeInfo = GetJsonTypeInfo(serializerOptions, type);
    readonly Encoding SelectedEncoding = selectedEncoding;
    readonly HttpContext httpContext = httpContext;
    readonly EnumerableType OutputType = OutputType;


    static IDictionary<Type, EnumerableType>? _targetInterface;
    static IDictionary<Type, EnumerableType> TargetInterfaces => _targetInterface ??= new Dictionary<Type, EnumerableType>()
    {
        {typeof(IAsyncEnumerable<>), EnumerableType.AsyncEnumerable},
        {typeof(IEnumerable<>), EnumerableType.Enumerable },
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
    public async Task WriteResponseBodyAsync(CancellationToken cancellationToken)
    {

        var method =
          OutputType switch
          {
              EnumerableType.AsyncEnumerable => WriteAsyncEnumerableMethod,
              EnumerableType.Enumerable => WriteEnumerableMethod,
              _ => throw new InvalidOperationException(),
          } ?? throw new InvalidOperationException();
        method = method.MakeGenericMethod(ObjectType, type);

        var result = (Task)method
          .Invoke(this, [Object, httpContext, JsonTypeInfo, SerializerOptions, SelectedEncoding, Begin, End, cancellationToken])!;

        await result;
    }

    static MethodInfo? writeEnumerableMethod;
    static MethodInfo WriteEnumerableMethod =>
        writeEnumerableMethod ??= typeof(InternalFormatWriter)
        .GetMethod(nameof(WriteEnumerableAsync), BindingFlags.Static | BindingFlags.Public)!;
    static MethodInfo? writeAsyncEnumerableMethod;
    static MethodInfo WriteAsyncEnumerableMethod =>
        writeAsyncEnumerableMethod ??= typeof(InternalFormatWriter)
        .GetMethod(nameof(WriteAsyncEnumerableAsync), BindingFlags.Static | BindingFlags.Public)!;

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
Justification = "The 'JsonSerializer.IsReflectionEnabledByDefault' feature switch, which is set to false by default for trimmed ASP.NET apps, ensures the JsonSerializer doesn't use Reflection.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode", Justification = "See above.")]
    public static async Task WriteEnumerableAsync<Enumerable, T>(Enumerable values, HttpContext httpContext, JsonTypeInfo<T>? JsonTypeInfo, JsonSerializerOptions SerializerOptions, Encoding SelectedEncoding, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken)
  where Enumerable : IEnumerable<T>
    {
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
    public static async Task WriteAsyncEnumerableAsync<AsyncEnumerable, T>(
        AsyncEnumerable values, HttpContext httpContext, JsonTypeInfo<T>? JsonTypeInfo, JsonSerializerOptions SerializerOptions, Encoding SelectedEncoding, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End, CancellationToken cancellationToken)
      where AsyncEnumerable : IAsyncEnumerable<T>
    {
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