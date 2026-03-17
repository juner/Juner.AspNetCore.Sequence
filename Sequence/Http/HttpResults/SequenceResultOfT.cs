using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.Net.Mime;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

[DebuggerDisplay("{Values,nq}")]
public partial class SequenceResult<T> : IStatusCodeHttpResult, ISequenceHttpResult, ISequenceHttpResult<T>, IEndpointMetadataProvider
{
    /// <summary>
    /// usage sequence values
    /// </summary>
    readonly object? _values = null;

    /// <summary>
    /// default contentType
    /// </summary>
    readonly string _contentType;

    internal object? Values => _values;

    internal async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_values is IAsyncEnumerable<T> asyncEnumerable)
        {
            await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
                yield return item;
            yield break;
        }
        if (_values is ChannelReader<T> reader)
        {
            while (await reader.WaitToReadAsync(cancellationToken))
                if (reader.TryRead(out var item))
                    yield return item;
            yield break;
        }
        if (_values is IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
                yield return item;
            yield break;
        }

    }

    /// <summary>
    /// 
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    void ThrowIfInvalidContentType()
    {
        if (!string.IsNullOrEmpty(_contentType))
        {
            if (!MakePatternList.Any(x => x.ContentType.Equals(_contentType, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"invalid contentType: {_contentType}");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    /// <param name="contentType"></param>
    public SequenceResult(IEnumerable<T> values, string? contentType = null)
    {
        (_values, _contentType) = (values, contentType ?? MediaTypeNames.Application.Json);
        ThrowIfInvalidContentType();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    /// <param name="contentType"></param>
    public SequenceResult(IAsyncEnumerable<T> values, string? contentType = null)
    {
        (_values, _contentType) = (values, contentType ?? MediaTypeNames.Application.Json);
        ThrowIfInvalidContentType();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    /// <param name="contentType"></param>
    public SequenceResult(ChannelReader<T> values, string? contentType = null)
    {
        (_values, _contentType) = (values, contentType ?? MediaTypeNames.Application.Json);
        ThrowIfInvalidContentType();
    }

    const int STATUS_CODE = StatusCodes.Status200OK;
    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public int StatusCode => STATUS_CODE;

    /// <inheritdoc/>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(new ProducesSequenceResponseTypeMetadata(
            STATUS_CODE,
            typeof(T),
            [.. MakePatternList.Select(v => v.ContentType)]));
    }

    record MakePattern(string ContentType, ReadOnlyMemory<byte> Begin, ReadOnlyMemory<byte> End);
    static bool TryGetPattern(StringSegment contentType, out ReadOnlyMemory<byte> begin, out ReadOnlyMemory<byte> end)
    {
        begin = default;
        end = default;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsedValue))
            return false;
        foreach (var (mediaType, begin2, end2) in MakePatternList)
            if (parsedValue.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase) == true)
            {
                (begin, end) = (begin2, end2);
                return true;
            }

        return false;
    }
    static bool TrySelectPattern(
        HttpContext ctx,
        string defaultContentType,
        out string selectedContentType,
        out ReadOnlyMemory<byte> begin,
        out ReadOnlyMemory<byte> end)
    {
        begin = default;
        end = default;
        selectedContentType = defaultContentType;

        var accepts = MediaTypeHeaderValue.ParseList(ctx.Request.Headers.Accept);

        if (accepts is { Count: > 0 })
        {
            foreach (var accept in accepts.OrderByDescending(a => a.Quality ?? 1))
            {
                if (TryGetPattern(accept.MediaType, out begin, out end))
                {
                    selectedContentType = accept.MediaType.ToString();
                    return true;
                }
                if (accept.MediaType == "*/*")
                {
                    return TryGetPattern(defaultContentType, out begin, out end);
                }
            }
            return false;
        }
        return TryGetPattern(defaultContentType, out begin, out end);
    }
    static MakePattern[]? _makePatternList;
    static MakePattern[] MakePatternList => _makePatternList ??= [.. MakePatterns()];
    static IEnumerable<MakePattern> MakePatterns()
    {
        byte[] RS = [.. "\u001e"u8];
        byte[] LF = [.. "\n"u8];

        {
            const string contentType =
#if NET8_0_OR_GREATER
                MediaTypeNames.Application.JsonSequence;
#else
                "application/json-seq";
#endif
            yield return new(contentType, RS, LF);
        }
        {
            const string contentType =
                "application/x-ndjson";
            yield return new(contentType, default, LF);
        }
        {
            const string contentType =
                "application/jsonl";
            yield return new(contentType, default, LF);
        }
        {
            const string contentType =
                "application/json";
            yield return new(contentType, default, default);
        }

    }


    static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "not register IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>"
        )]
        public static partial void LogNotHaveJsonOptions(ILogger logger);
    }
}