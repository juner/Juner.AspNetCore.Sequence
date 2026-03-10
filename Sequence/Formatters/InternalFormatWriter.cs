using Microsoft.AspNetCore.Mvc.Formatters;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;


#if NET9_0_OR_GREATER
using System.IO.Pipelines;
#endif

namespace Juner.AspNetCore.Sequence.Formatters;

internal class InternalFormatWriter(JsonSerializerOptions serializerOptions, Encoding selectedEncoding, HttpContext httpContext, OutputType OutputType, Type type)
{
    public InternalFormatWriter(OutputFormatterWriteContext context)
    {
        
    }
    public ReadOnlyMemory<byte> Begin = default;
    public ReadOnlyMemory<byte> End = default;
    readonly JsonSerializerOptions SerializerOptions = serializerOptions;
    readonly JsonTypeInfo? JsonTypeInfo = GetJsonTypeInfo(serializerOptions, type);
    readonly Encoding SelectedEncoding = selectedEncoding;
    readonly HttpContext httpContext = httpContext;


    static IDictionary<Type, OutputType>? _targetInterface;
    static IDictionary<Type, OutputType> TargetInterfaces => _targetInterface ??= new Dictionary<Type, OutputType>()
    {
        {typeof(IAsyncEnumerable<>), OutputType.AsyncEnumerable},
        {typeof(IEnumerable<>), OutputType.Enumerable },
    }.AsReadOnly();
    public static bool TryGetOutputMode(Type? objectType, [NotNullWhen(true)] out OutputType outputType, [NotNullWhen(true)] out Type type)
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
    public async Task WriteResponseBodyAsync(object Object, Type objectType, CancellationToken cancellationToken)
    {

        var provider = httpContext.RequestServices;
        if (!TryGetOutputMode(objectType, out var outputType, out var type))
            throw new InvalidOperationException();

        var response = httpContext.Response;
        var method =
          outputType switch
          {
              OutputType.AsyncEnumerable => WriteAsyncEnumerableMethod,
              OutputType.Enumerable => WriteEnumerableMethod,
              _ => throw new InvalidOperationException(),
          } ?? throw new InvalidOperationException();
        method = method.MakeGenericMethod(objectType, type);

        var result = method
          .Invoke(this, [Object, cancellationToken])
           as Task
           ?? throw new InvalidOperationException();

        await result;
    }

    static MethodInfo? writeEnumerableMethod;
    static MethodInfo WriteEnumerableMethod =>
        writeEnumerableMethod ??= typeof(InternalFormatWriter)
        .GetMethod(nameof(WriteEnumerable), BindingFlags.Instance | BindingFlags.NonPublic)!;
    static MethodInfo? writeAsyncEnumerableMethod;
    static MethodInfo WriteAsyncEnumerableMethod =>
        writeAsyncEnumerableMethod ??= typeof(InternalFormatWriter)
        .GetMethod(nameof(WriteAsyncEnumerable), BindingFlags.Instance | BindingFlags.NonPublic)!;
    public async Task WriteEnumerable<Enumerable, T>(Enumerable values, CancellationToken cancellationToken)
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
                        await WriteRecordAsync(responseWriter, value, JsonTypeInfo, cancellationToken);
                }
#else
                {
                    var stream = httpContext.Response.Body;
                    foreach (var value in values)
                        await WriteRecordAsync(stream, value, JsonTypeInfo, cancellationToken);
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
                    await WriteRecordAsync(transcodingStream, value, JsonTypeInfo, cancellationToken);
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
    public async Task WriteAsyncEnumerable<AsyncEnumerable, T>(AsyncEnumerable values, CancellationToken cancellationToken)
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
                        await WriteRecordAsync(responseWriter, value, JsonTypeInfo, cancellationToken);
                }
#else
        {
            var stream = httpContext.Response.Body;
            await foreach (var value in values)
                await WriteRecordAsync(stream, value, JsonTypeInfo, cancellationToken);
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
                    await WriteRecordAsync(transcodingStream, value, JsonTypeInfo, cancellationToken);
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
    async ValueTask WriteRecordAsync<T>(PipeWriter writer, T value, JsonTypeInfo? jsonTypeInfo, CancellationToken cancellationToken)
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
    async ValueTask WriteRecordAsync<T>(Stream writer, T value, JsonTypeInfo? jsonTypeInfo, CancellationToken cancellationToken = default)
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
