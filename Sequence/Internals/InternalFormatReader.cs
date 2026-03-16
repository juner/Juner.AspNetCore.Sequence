using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Internals;

internal class InternalFormatReader
{
    #region ReadResult
    public static object ReadResult<T>(
        EnumerableType enumerableType,
        PipeReader reader,
        JsonTypeInfo jsonTypeInfo,
        ReadOnlyMemory<byte>[] start,
        ReadOnlyMemory<byte>[] end,
        CancellationToken cancellationToken)
    {
        var asyncEnumerable = GetAsyncEnumerable(reader, (JsonTypeInfo<T>)jsonTypeInfo, start, end, cancellationToken);
        return enumerableType switch
        {
            EnumerableType.AsyncEnumerable => asyncEnumerable,
            EnumerableType.Sequence => new Http.Sequence<T>(asyncEnumerable),
            EnumerableType.Enumerable => GetEnumerableAsync(asyncEnumerable, cancellationToken),
            EnumerableType.Array => GetArrayAsync(asyncEnumerable, cancellationToken),
            EnumerableType.List => GetListAsync(asyncEnumerable, cancellationToken),
            EnumerableType.ChannelReader => GetChannelReader(asyncEnumerable, cancellationToken),
            _ => throw new InvalidOperationException($"type:{enumerableType} is not support"),
        };
    }

    public static object ReadResult(
        Type elementType,
        EnumerableType enumerableType,
        PipeReader reader,
        JsonTypeInfo jsonTypeInfo,
        ReadOnlyMemory<byte>[] start,
        ReadOnlyMemory<byte>[] end,
        CancellationToken cancellationToken
    )
    {
        var del = cache.GetOrAdd(elementType, CreateDelegate);

        var func =
            (Func<
                EnumerableType,
                PipeReader,
                JsonTypeInfo,
                ReadOnlyMemory<byte>[],
                ReadOnlyMemory<byte>[],
                CancellationToken,
                object>)del;

        return func(
            enumerableType,
            reader,
            jsonTypeInfo,
            start,
            end,
            cancellationToken);
    }

    static readonly ConcurrentDictionary<Type, Delegate> cache = new();

    static Delegate CreateDelegate(Type elementType)
    {
        var method =
            typeof(InternalFormatReader)
            .GetMethods()
            .First(static v => v is
            {
                Name: nameof(ReadResult),
                IsGenericMethod: true
            } && v.GetParameters() is { Length: 6 })
            .MakeGenericMethod(elementType);

        return method.CreateDelegate(
            typeof(Func<
                EnumerableType,
                PipeReader,
                JsonTypeInfo,
                ReadOnlyMemory<byte>[],
                ReadOnlyMemory<byte>[],
                CancellationToken,
                object>));
    }
    #endregion

    public static async Task<IEnumerable<T>> GetEnumerableAsync<T>(
        IAsyncEnumerable<T> asyncEnumerable,
        CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        return await asyncEnumerable.ToListAsync(cancellationToken);
#else
        List<T>? list = null;
        await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
            (list ??= []).Add(item);
        return list ?? [];
#endif
    }

    public static async Task<List<T>> GetListAsync<T>(
        IAsyncEnumerable<T> asyncEnumerable,
        CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        return await asyncEnumerable.ToListAsync(cancellationToken);
#else
        List<T>? list = null;
        await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
            (list ??= []).Add(item);
        return list ?? [];
#endif
    }

    public static async Task<T[]> GetArrayAsync<T>(
        IAsyncEnumerable<T> asyncEnumerable,
        CancellationToken cancellationToken)
    {
#if NET10_0_OR_GREATER
        return await asyncEnumerable.ToArrayAsync(cancellationToken);
#else
        List<T>? list = null;
        await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
            (list ??= []).Add(item);
        return list?.ToArray() ?? Array.Empty<T>();
#endif
    }

    public static async IAsyncEnumerable<T> GetAsyncEnumerable<T>(
         PipeReader reader,
         JsonTypeInfo<T> jsonTypeInfo,
         ReadOnlyMemory<byte>[] start,
         ReadOnlyMemory<byte>[] end,
         [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TryReadFrame tryReadFrame = (start, end) switch
        {
            ({ Length: 0 }, [{ Length: 1 }]) => TryReadFrameEndByteOnly,
            ([{ Length: 1 }], [{ Length: 1 }]) => TryReadFrameStartEndByteOnly,
            _ => TryReadFrameAny,
        };
        while (true)
        {
            var result = await reader.ReadAsync(cancellationToken);
            var buffer = result.Buffer;

            while (tryReadFrame(ref buffer, start, end, out var frame))
            {
                Utf8JsonReader jsonReader = frame is { IsSingleSegment: true } ? new(frame.FirstSpan) : new(frame);

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
    delegate bool TryReadFrame(
        ref ReadOnlySequence<byte> buffer,
        ReadOnlyMemory<byte>[] start,
        ReadOnlyMemory<byte>[] end,
        out ReadOnlySequence<byte> frame);

    static bool TryReadFrameEndByteOnly(
        ref ReadOnlySequence<byte> buffer,
        ReadOnlyMemory<byte>[] start,
        ReadOnlyMemory<byte>[] end,
        out ReadOnlySequence<byte> frame)
    {
        var e = end[0].Span[0];
        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out frame, e))
            return false;

        buffer = buffer.Slice(reader.Position);

        return true;
    }

    static bool TryReadFrameStartEndByteOnly(
         ref ReadOnlySequence<byte> buffer,
         ReadOnlyMemory<byte>[] start,
         ReadOnlyMemory<byte>[] end,
         out ReadOnlySequence<byte> frame)
    {
        frame = default;
        var s = start[0].Span[0];
        var e = end[0].Span[0];

        var reader = new SequenceReader<byte>(buffer);

        if (!reader.IsNext(s, advancePast: true))
            return false;

        if (!reader.TryReadTo(out frame, e))
            return false;

        buffer = buffer.Slice(reader.Position);

        return true;
    }

    static bool TryReadFrameAny(
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

    public static async Task<ChannelReader<T>> GetChannelReader<T>(
        IAsyncEnumerable<T> asyncEnumerable,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<T>();

        _ = Task.Run(async () =>
        {
            Exception? error = null;
            try
            {
                await foreach (var item in asyncEnumerable.WithCancellation(cancellationToken))
                    await channel.Writer.WriteAsync(item);
            }
            catch (Exception error2)
            {
                error = error2;
            }
            finally
            {
                channel.Writer.Complete(error);
            }
        }, cancellationToken);

        return channel.Reader;
    }
}