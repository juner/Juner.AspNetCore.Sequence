using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MediaTypeHeaderValue = Microsoft.Net.Http.Headers.MediaTypeHeaderValue;
using MediaTypeNames = System.Net.Mime.MediaTypeNames;


namespace Juner.AspNetCore.Sequence.Http;

public partial class Sequence<T>: IBindableFromHttpContext<Sequence<T>>
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
        var mediaTypeHeaderValue = MediaTypeHeaderValue.Parse(request.ContentType);

        var jsonTypeInfo = (JsonTypeInfo<T>)serializerOptions.GetTypeInfo(typeof(T));

        foreach (var (match, func) in MakePatternActionList)
            if (match(mediaTypeHeaderValue))
                return func(mediaTypeHeaderValue, jsonTypeInfo, request, cancellationToken);
        return default;
    }

    #region MakePatternAction

    #region delimiters

    static readonly byte[] RS = [.."\u001e"u8];
    static readonly byte[] LF = [.."\n"u8];

    static readonly ReadOnlyMemory<byte>[] JSONSEQ_START = [RS];
    static readonly ReadOnlyMemory<byte>[] JSONSEQ_END = [LF];

    static readonly ReadOnlyMemory<byte>[] NDJSON_START = [];
    static readonly ReadOnlyMemory<byte>[] NDJSON_END = [LF];

    #endregion

    static (Func<MediaTypeHeaderValue, bool>, Func<MediaTypeHeaderValue, JsonTypeInfo<T>, HttpRequest, CancellationToken, Sequence<T>>)[]? _makePatternActionList = null;
    static (Func<MediaTypeHeaderValue, bool>, Func<MediaTypeHeaderValue, JsonTypeInfo<T>, HttpRequest, CancellationToken, Sequence<T>>)[] MakePatternActionList => _makePatternActionList ??= [.. MakePatternActions()];

    static IEnumerable<(Func<MediaTypeHeaderValue, bool>, Func<MediaTypeHeaderValue, JsonTypeInfo<T>, HttpRequest, CancellationToken, Sequence<T>>)> MakePatternActions()
    {
        {
            const string contentType =
#if NET8_0_OR_GREATER
                MediaTypeNames.Application.JsonSequence;
#else
                "application/json-seq";
#endif
            yield return (IsJsonSeq, JsonSeq);
            static bool IsJsonSeq(MediaTypeHeaderValue mediaTypeHeaderValue) => mediaTypeHeaderValue.MediaType.StartsWith(contentType, StringComparison.OrdinalIgnoreCase) == true;
            static Sequence<T> JsonSeq(MediaTypeHeaderValue mediaTypeHeaderValue, JsonTypeInfo<T> jsonTypeInfo, HttpRequest request, CancellationToken cancellationToken)
            {
                if ((mediaTypeHeaderValue.Encoding ?? Encoding.UTF8).CodePage != Encoding.UTF8.CodePage)
                    throw new NotSupportedException($"{mediaTypeHeaderValue.MediaType} is not support charset");
                return new Sequence<T>(GetAsyncEnumerable(
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
                yield return (IsNdJson, JsonLine);
                static bool IsNdJson(MediaTypeHeaderValue mediaTypeHeaderValue) => mediaTypeHeaderValue.MediaType.StartsWith(contentType, StringComparison.OrdinalIgnoreCase) == true;


            }
            {
                // application/jsonl support
                const string contentType = "application/jsonl";
                yield return (IsJsonLine, JsonLine);
                static bool IsJsonLine(MediaTypeHeaderValue mediaTypeHeaderValue) => mediaTypeHeaderValue.MediaType.StartsWith(contentType, StringComparison.OrdinalIgnoreCase) == true;
            }
            static Sequence<T> JsonLine(MediaTypeHeaderValue mediaTypeHeaderValue, JsonTypeInfo<T> jsonTypeInfo, HttpRequest request, CancellationToken cancellationToken)
            {
                if ((mediaTypeHeaderValue.Encoding ?? Encoding.UTF8).CodePage != Encoding.UTF8.CodePage)
                    throw new NotSupportedException($"{mediaTypeHeaderValue.MediaType} is not support charset");

                return new(GetAsyncEnumerable(
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
            yield return new(IsJson, Json);
            static bool IsJson(MediaTypeHeaderValue mediaTypeHeaderValue)
                => mediaTypeHeaderValue.MediaType.StartsWith(contentType, StringComparison.OrdinalIgnoreCase) == true
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
    static async IAsyncEnumerable<T> GetAsyncEnumerable(
         PipeReader reader,
         JsonTypeInfo<T> jsonTypeInfo,
         ReadOnlyMemory<byte>[] start,
         ReadOnlyMemory<byte>[] end,
         [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            while (TryReadFrame(ref buffer, start, end, out var frame))
            {
                var jsonReader = new Utf8JsonReader(frame);

                var value = JsonSerializer.Deserialize(ref jsonReader, jsonTypeInfo);

                if (value is not null)
                    yield return value;
            }

            if (result.IsCompleted)
            {
                if (!buffer.IsEmpty)
                {
                    if (TryReadLastFrame(ref buffer, start, out var frame))
                    {
                        var jsonReader = new Utf8JsonReader(frame);

                        var value = JsonSerializer.Deserialize(ref jsonReader, jsonTypeInfo);

                        if (value is not null)
                            yield return value;
                    }
                }

                reader.AdvanceTo(buffer.End);
                yield break;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    static bool TryReadFrame(
         ref ReadOnlySequence<byte> buffer,
         ReadOnlyMemory<byte>[] start,
         ReadOnlyMemory<byte>[] end,
         out ReadOnlySequence<byte> frame)
    {
        frame = default;

        var reader = new SequenceReader<byte>(buffer);

        if (!MatchStart(ref reader, start))
            return false;

        if (!TryReadToAny(ref reader, end, out frame))
            return false;

        buffer = buffer.Slice(reader.Position);

        return true;
    }

    static bool MatchStart(ref SequenceReader<byte> reader, ReadOnlyMemory<byte>[] start)
    {
        if (start.Length == 0)
            return true;

        foreach (var s in start)
        {
            if (reader.IsNext(s.Span, advancePast: true))
                return true;
        }

        return false;
    }

    static bool TryReadToAny(
        ref SequenceReader<byte> reader,
        ReadOnlyMemory<byte>[] delimiters,
        out ReadOnlySequence<byte> frame)
    {
        frame = default;

        if (delimiters.Length == 0)
            return false;

        Span<byte> firstBytes = stackalloc byte[delimiters.Length];

        for (var i = 0; i < delimiters.Length; i++)
            firstBytes[i] = delimiters[i].Span[0];

        var start = reader.Position;

        while (reader.TryAdvanceToAny(firstBytes, advancePastDelimiter: false))
        {
            var pos = reader.Position;

            foreach (var d in delimiters)
            {
                if (reader.IsNext(d.Span, advancePast: true))
                {
                    frame = reader.Sequence.Slice(start, pos);
                    return true;
                }
            }

            reader.Advance(1);
        }

        return false;
    }

    static bool TryReadLastFrame(
        ref ReadOnlySequence<byte> buffer,
        ReadOnlyMemory<byte>[] start,
        out ReadOnlySequence<byte> frame)
    {
        var reader = new SequenceReader<byte>(buffer);

        if (!MatchStart(ref reader, start))
        {
            frame = default;
            return false;
        }

        frame = buffer.Slice(reader.Position);

        buffer = buffer.Slice(buffer.End);

        return true;
    }

    #endregion
}
