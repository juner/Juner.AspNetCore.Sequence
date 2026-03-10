using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

#if NET9_0_OR_GREATER
using System.IO.Pipelines;
#endif

namespace Juner.AspNetCore.JsonSequence.Formatters;

/// <summary>
/// application/json-seq 対応の フォーマッター
/// </summary>
public class JsonSequenceOutputFormatter : TextOutputFormatter
{
    /// <summary>
    /// 
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; }

    /// <summary>
    /// 
    /// </summary>
    public JsonSequenceOutputFormatter(JsonSerializerOptions jsonSerializerOptions)
    {
        SerializerOptions = jsonSerializerOptions;
#if NET8_0_OR_GREATER
        jsonSerializerOptions.MakeReadOnly();
#endif
        SupportedMediaTypes.Add(ContentType);
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    internal static JsonSequenceOutputFormatter CreateFormatter(JsonOptions jsonOptions)
    {
        var jsonSerializerOptions = jsonOptions.JsonSerializerOptions;

        if (jsonSerializerOptions.Encoder is null)
        {
            // If the user hasn't explicitly configured the encoder, use the less strict encoder that does not encode all non-ASCII characters.
            jsonSerializerOptions = new JsonSerializerOptions(jsonSerializerOptions)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
        }

        return new JsonSequenceOutputFormatter(jsonSerializerOptions);
    }


    const string ContentType =
#if NET8_0_OR_GREATER
        MediaTypeNames.Application.JsonSequence;
#else
        "application/json-seq";
#endif

    /// <inheritdoc />
    protected override bool CanWriteType(Type? type)
      => TryGetOutputMode(type, out _, out _);

    /// <inheritdoc/>
    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        if (!TryGetOutputMode(context.ObjectType, out _, out _))
            return false;

        var accept = context.HttpContext.Request.GetTypedHeaders().Accept;

        if (accept == null || accept.Count == 0)
            return false;

        return accept.Any(v => v.MediaType == ContentType);
    }

    /// <inheritdoc cref="TextOutputFormatter.WriteResponseBodyAsync(OutputFormatterWriteContext, Encoding)" />
    public sealed override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedEncoding);

        var httpContext = context.HttpContext;

        var objectType = context.ObjectType!;

        var provider = context.HttpContext.RequestServices;
        if (!TryGetOutputMode(context.ObjectType, out var outputType, out var type))
            throw new InvalidOperationException();

        JsonTypeInfo? jsonTypeInfo = null;
        {
            var declaredTypeJsonInfo = SerializerOptions.GetTypeInfo(type);

            var runtimeType = type;
            if (declaredTypeJsonInfo.ShouldUseWith(runtimeType))
            {
                jsonTypeInfo = declaredTypeJsonInfo;
            }
        }

        var jsonOptions = SerializerOptions;
        var cancellationToken = context.HttpContext.RequestAborted;

        var response = context.HttpContext.Response;
        var method =
          outputType switch
          {
              OutputType.AsyncEnumerable => WriteAsyncEnumerableMethod,
              OutputType.Enumerable => WriteEnumerableMethod,
              _ => throw new InvalidOperationException(),
          } ?? throw new InvalidOperationException();
        method = method.MakeGenericMethod(objectType, type);

        var result = method
          .Invoke(this, [context.Object, context, jsonTypeInfo, selectedEncoding, cancellationToken])
           as Task
           ?? throw new InvalidOperationException();

        await result;
    }
    static MethodInfo? writeEnumerableMethod;
    static MethodInfo WriteEnumerableMethod =>
        writeEnumerableMethod ??= typeof(JsonSequenceOutputFormatter)
        .GetMethod(nameof(WriteEnumerable), BindingFlags.Instance | BindingFlags.NonPublic)!;
    static MethodInfo? writeAsyncEnumerableMethod;
    static MethodInfo WriteAsyncEnumerableMethod =>
        writeAsyncEnumerableMethod ??= typeof(JsonSequenceOutputFormatter)
        .GetMethod(nameof(WriteAsyncEnumerable), BindingFlags.Instance | BindingFlags.NonPublic)!;


    /// <summary>
    /// 
    /// </summary>
    public enum OutputType
    {
        /// <summary>
        /// 
        /// </summary>
        AsyncEnumerable = 0,

        /// <summary>
        /// 
        /// </summary>
        Enumerable = 1,
    }

    static Dictionary<Type, OutputType>? _targetInterface;
    static Dictionary<Type, OutputType> TargetInterfaces => _targetInterface ??= new()
    {
        {typeof(IAsyncEnumerable<>), OutputType.AsyncEnumerable},
        {typeof(IEnumerable<>), OutputType.Enumerable },
    };

    /// <summary>
    /// 
    /// </summary>
    /// <param name="objectType"></param>
    /// <param name="outputType"></param>
    /// <param name="type"></param>
    /// <returns></returns>
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
    #region RS
    static ReadOnlyMemory<byte>? _rs;
    static ReadOnlyMemory<byte> RS => _rs ??= "\u001e"u8.ToArray();
    #endregion
    #region LF
    static ReadOnlyMemory<byte>? _lf;
    static ReadOnlyMemory<byte> LF => _lf ??= "\n"u8.ToArray();
    #endregion
    async Task WriteEnumerable<Enumerable, T>(Enumerable values, OutputFormatterWriteContext context, JsonTypeInfo? jsonTypeInfo, Encoding selectedEncoding, CancellationToken cancellationToken)
      where Enumerable : IEnumerable<T>
    {
        var httpContext = context.HttpContext;
        if (selectedEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            try
            {
#if NET9_0_OR_GREATER
                {
                    var responseWriter = httpContext.Response.BodyWriter;
                    foreach (var value in values)
                        await WriteRecordAsync(responseWriter, value, jsonTypeInfo, cancellationToken);
                }
#else
                {
                    var stream = httpContext.Response.Body;
                    foreach (var value in values)
                        await WriteRecordAsync(stream, value, jsonTypeInfo, cancellationToken);
                }
#endif
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        else
        {
            var transcodingStream = Encoding.CreateTranscodingStream(httpContext.Response.Body, selectedEncoding, Encoding.UTF8, leaveOpen: true);

            ExceptionDispatchInfo? exceptionDispatchInfo = null;
            try
            {
                foreach (var value in values)
                    await WriteRecordAsync(transcodingStream, value, jsonTypeInfo, cancellationToken);
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
    async Task WriteAsyncEnumerable<AsyncEnumerable, T>(AsyncEnumerable values, OutputFormatterWriteContext context, JsonTypeInfo? jsonTypeInfo, Encoding selectedEncoding, CancellationToken cancellationToken)
      where AsyncEnumerable : IAsyncEnumerable<T>
    {
        var httpContext = context.HttpContext;
        if (selectedEncoding.CodePage == Encoding.UTF8.CodePage)
        {
            try
            {
#if NET9_0_OR_GREATER
                {
                    var responseWriter = httpContext.Response.BodyWriter;
                    await foreach (var value in values)
                        await WriteRecordAsync(responseWriter, value, jsonTypeInfo, cancellationToken);
                }
#else
        {
            var stream = httpContext.Response.Body;
            await foreach (var value in values)
                await WriteRecordAsync(stream, value, jsonTypeInfo, cancellationToken);
        }
#endif
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
        else
        {
            var transcodingStream = Encoding.CreateTranscodingStream(httpContext.Response.Body, selectedEncoding, Encoding.UTF8, leaveOpen: true);

            ExceptionDispatchInfo? exceptionDispatchInfo = null;
            try
            {
                await foreach (var value in values)
                    await WriteRecordAsync(transcodingStream, value, jsonTypeInfo, cancellationToken);
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
        await writer.WriteAsync(RS, cancellationToken);
        if (jsonTypeInfo is not null)
            await JsonSerializer.SerializeAsync(writer, value, jsonTypeInfo, cancellationToken);
        else
            await JsonSerializer.SerializeAsync(writer, value, SerializerOptions, cancellationToken);
        await writer.WriteAsync(LF, cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
#endif
    async ValueTask WriteRecordAsync<T>(Stream writer, T value, JsonTypeInfo? jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        await writer.WriteAsync(RS, cancellationToken);
        if (jsonTypeInfo is not null)
            await JsonSerializer.SerializeAsync(writer, value, jsonTypeInfo, cancellationToken);
        else
            await JsonSerializer.SerializeAsync(writer, value, SerializerOptions, cancellationToken);
        await writer.WriteAsync(LF, cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

}

file static class JsonTypeInfoExtensions
{
    public static bool HasKnownPolymorphism(this JsonTypeInfo jsonTypeInfo)
       => jsonTypeInfo.Type.IsSealed || jsonTypeInfo.Type.IsValueType || jsonTypeInfo.PolymorphismOptions is not null;
    public static bool ShouldUseWith(this JsonTypeInfo jsonTypeInfo, [NotNullWhen(false)] Type? runtimeType)
       => runtimeType is null || jsonTypeInfo.Type == runtimeType || jsonTypeInfo.HasKnownPolymorphism();
}
