using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Buffers;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;

namespace Juner.AspNetCore.Sequence.Http;

public sealed partial class Sequence<T> : IBindableFromHttpContext<Sequence<T>>, IAsyncEnumerable<T>
{
    readonly object? _values;
    public Sequence(IAsyncEnumerable<T> values) => _values = values;
    public Sequence(ChannelReader<T> values) => _values = values;
    public Sequence(IEnumerable<T> values) => _values = values;

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
    #region delimiters

    static readonly byte[] RS = "\u001e"u8.ToArray();
    static readonly byte[] LF = "\n"u8.ToArray();

    static readonly ReadOnlyMemory<byte>[] JSONSEQ_START = [RS];
    static readonly ReadOnlyMemory<byte>[] JSONSEQ_END = [LF];

    static readonly ReadOnlyMemory<byte>[] NDJSON_START = [];
    static readonly ReadOnlyMemory<byte>[] NDJSON_END = [LF];

    #endregion

    static async ValueTask<Sequence<T>?> IBindableFromHttpContext<Sequence<T>>.BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var logger = GetLogger(context.RequestServices);
        var serializerOptions = GetOptions(context.RequestServices, logger);
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        var cancellationToken = context.RequestAborted;
        var request = context.Request;
        var contentType = request.ContentType;

        var jsonTypeInfo = (JsonTypeInfo<T>)serializerOptions.GetTypeInfo(typeof(T));

        if (contentType?.StartsWith("application/json-seq", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (GetCharset(contentType).CodePage != Encoding.UTF8.CodePage)
                throw new NotSupportedException("application/json-seq is not support charset");
            return new(GetAsyncEnumerable(
                request.BodyReader,
                jsonTypeInfo,
                JSONSEQ_START,
                JSONSEQ_END,
                cancellationToken));
        }
        var isNdJson = contentType?.StartsWith("application/x-ndjson", StringComparison.OrdinalIgnoreCase);
        var isJsonL = contentType?.StartsWith("application/jsonl", StringComparison.OrdinalIgnoreCase);
        if (isNdJson == true ||
            isJsonL == true)
        {
            if (GetCharset(contentType).CodePage != Encoding.UTF8.CodePage)
                throw new NotSupportedException($"{(isNdJson == true ? "application/x-ndjson" : "application/jsonl")} is not support charset");
            return new(GetAsyncEnumerable(
                request.BodyReader,
                jsonTypeInfo,
                NDJSON_START,
                NDJSON_END,
                cancellationToken));
        }

        if (contentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var encoding = GetCharset(contentType);

            var stream =
                encoding.CodePage == Encoding.UTF8.CodePage
                ? request.Body
                : Encoding.CreateTranscodingStream(request.Body, encoding, Encoding.UTF8);

            var asyncEnumerable = JsonSerializer.DeserializeAsyncEnumerable(stream, jsonTypeInfo, cancellationToken);

            return new(Filter(asyncEnumerable, cancellationToken));
        }

        return default;
    }


    static Encoding GetCharset(string? contentType)
    {
        if (contentType == null) return Encoding.UTF8;

        var charset = MediaTypeHeaderValue.Parse(contentType).Charset;
        if (charset.HasValue && !string.IsNullOrEmpty(charset.Value))
            return Encoding.GetEncoding(charset.Value);
        return Encoding.UTF8;
    }
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



    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (_values is IAsyncEnumerable<T> asyncEnumerable)
        {
            await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
                yield return item;
            yield break;
        }
        if (_values is ChannelReader<T> channelReader)
        {
            while (await channelReader.WaitToReadAsync(cancellationToken))
                if (channelReader.TryRead(out var item))
                    yield return item;
            yield break;
        }
        if (_values is IEnumerable<T> enumerable)
        {
            foreach (var item in enumerable)
                yield return item;
        }
        yield break;

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