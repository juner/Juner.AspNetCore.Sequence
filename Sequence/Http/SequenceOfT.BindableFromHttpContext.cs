using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MediaTypeHeaderValue = Microsoft.Net.Http.Headers.MediaTypeHeaderValue;
using MediaTypeNames = System.Net.Mime.MediaTypeNames;


namespace Juner.AspNetCore.Sequence.Http;

public partial class Sequence<T> : IBindableFromHttpContext<Sequence<T>>
{
    #region static BindAsync

    static ILogger GetLogger(IServiceProvider provider)
    {
        var logger = provider.GetService<ILogger<Sequence<T>>>();
        if (logger is not null) return logger;
        return NullLogger.Instance;
    }
    static JsonSerializerOptions GetOptions(IServiceProvider provider, ILogger logger)
    {
        var jsonOptions = provider.GetService<IOptions<JsonOptions>>()?.Value;
        if (jsonOptions is null)
        {
            Log.LogNotHaveJsonOptions(logger);
            jsonOptions = new JsonOptions();
        }
        return jsonOptions.SerializerOptions;
    }

    static async ValueTask<Sequence<T>?> IBindableFromHttpContext<Sequence<T>>.BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var logger = GetLogger(context.RequestServices);
        var serializerOptions = GetOptions(context.RequestServices, logger);
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        var cancellationToken = context.RequestAborted;
        var request = context.Request;

        if (string.IsNullOrEmpty(request.ContentType) || !MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaTypeHeaderValue))
            throw new ArgumentException("required context.Request.ContentType is parsable.");

        var jsonTypeInfo = (JsonTypeInfo<T>)serializerOptions.GetTypeInfo(typeof(T));

        foreach (var (match, func, _) in MakePatternActionList)
            if (match(mediaTypeHeaderValue))
                return func(mediaTypeHeaderValue, jsonTypeInfo, request, cancellationToken);
        return default;
    }

    #region MakePatternAction

    #region delimiters

    static readonly byte[] RS = [.. "\u001e"u8];
    static readonly byte[] LF = [.. "\n"u8];

    static readonly ReadOnlyMemory<byte>[] JSONSEQ_START = [RS];
    static readonly ReadOnlyMemory<byte>[] JSONSEQ_END = [LF];

    static readonly ReadOnlyMemory<byte>[] NDJSON_START = [];
    static readonly ReadOnlyMemory<byte>[] NDJSON_END = [LF];

    #endregion

    static PatternAction[]? _makePatternActionList = null;
    static PatternAction[] MakePatternActionList => _makePatternActionList ??= [.. MakePatternActions()];

    /// <summary>
    /// 
    /// </summary>
    /// <param name="Match"></param>
    /// <param name="Action"></param>
    record struct PatternAction(Func<MediaTypeHeaderValue, bool> Match, Func<MediaTypeHeaderValue, JsonTypeInfo<T>, HttpRequest, CancellationToken, Sequence<T>> Action, string ContentType);
    static IEnumerable<PatternAction> MakePatternActions()
    {
        {
            const string contentType =
#if NET8_0_OR_GREATER
                MediaTypeNames.Application.JsonSequence;
#else
                "application/json-seq";
#endif
            yield return new(IsJsonSeq, JsonSeq, contentType);
            static bool IsJsonSeq(MediaTypeHeaderValue mediaTypeHeaderValue) => mediaTypeHeaderValue.MediaType.Equals(contentType, StringComparison.OrdinalIgnoreCase) == true;
            static Sequence<T> JsonSeq(MediaTypeHeaderValue mediaTypeHeaderValue, JsonTypeInfo<T> jsonTypeInfo, HttpRequest request, CancellationToken cancellationToken)
            {
                if ((mediaTypeHeaderValue.Encoding ?? Encoding.UTF8).CodePage != Encoding.UTF8.CodePage)
                    throw new NotSupportedException($"{mediaTypeHeaderValue.MediaType} is not support charset");
                return new Sequence<T>(InternalFormatReader.GetAsyncEnumerable(
                    request.BodyReader,
                    jsonTypeInfo,
                    JSONSEQ_START,
                    JSONSEQ_END,
                    cancellationToken));
            }
        }
        {
            {
                // application/x-ndjson support
                const string contentType = "application/x-ndjson";
                yield return new(IsNdJson, JsonLine, contentType);
                static bool IsNdJson(MediaTypeHeaderValue mediaTypeHeaderValue) => mediaTypeHeaderValue.MediaType.Equals(contentType, StringComparison.OrdinalIgnoreCase) == true;
            }
            {
                // application/jsonl support
                const string contentType = "application/jsonl";
                yield return new(IsJsonLine, JsonLine, contentType);
                static bool IsJsonLine(MediaTypeHeaderValue mediaTypeHeaderValue) => mediaTypeHeaderValue.MediaType.Equals(contentType, StringComparison.OrdinalIgnoreCase) == true;
            }
            static Sequence<T> JsonLine(MediaTypeHeaderValue mediaTypeHeaderValue, JsonTypeInfo<T> jsonTypeInfo, HttpRequest request, CancellationToken cancellationToken)
            {
                if ((mediaTypeHeaderValue.Encoding ?? Encoding.UTF8).CodePage != Encoding.UTF8.CodePage)
                    throw new NotSupportedException($"{mediaTypeHeaderValue.MediaType} is not support charset");

                return new(InternalFormatReader.GetAsyncEnumerable(
                    request.BodyReader,
                    jsonTypeInfo,
                    NDJSON_START,
                    NDJSON_END,
                    cancellationToken));
            }
        }
        {
            // application/json and application/*+json support
            const string contentType = MediaTypeNames.Application.Json;
            const string contentTypeStart = "application/";
            const string contentTypeEnd = "+json";
            yield return new(IsJson, Json, contentType);
            static bool IsJson(MediaTypeHeaderValue mediaTypeHeaderValue)
                => mediaTypeHeaderValue.MediaType.Equals(contentType, StringComparison.OrdinalIgnoreCase) == true
                    || (
                        mediaTypeHeaderValue.MediaType.StartsWith(contentTypeStart, StringComparison.OrdinalIgnoreCase) == true
                        && mediaTypeHeaderValue.MediaType.EndsWith(contentTypeEnd, StringComparison.OrdinalIgnoreCase) == true
                    );

            static Sequence<T> Json(MediaTypeHeaderValue mediaTypeHeaderValue, JsonTypeInfo<T> jsonTypeInfo, HttpRequest request, CancellationToken cancellationToken)
            {
                var encoding = mediaTypeHeaderValue.Encoding ?? Encoding.UTF8;

                var stream =
                    encoding.CodePage == Encoding.UTF8.CodePage
                    ? request.Body
                    : Encoding.CreateTranscodingStream(request.Body, encoding, Encoding.UTF8);

                var asyncEnumerable = JsonSerializer.DeserializeAsyncEnumerable(stream, jsonTypeInfo, cancellationToken);

                return new(Filter(asyncEnumerable, cancellationToken));
            }
        }
    }
    #endregion

    static async IAsyncEnumerable<T> Filter(
        IAsyncEnumerable<T?> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            if (item is not null)
                yield return item;
        }
    }
    #endregion
}