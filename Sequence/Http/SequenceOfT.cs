using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http;

public sealed partial class Sequence<T> : IAsyncEnumerable<T>
{
    readonly object? _values;
    public Sequence(IAsyncEnumerable<T> values) => _values = values;
    public Sequence(ChannelReader<T> values) => _values = values;
    public Sequence(IEnumerable<T> values) => _values = values;
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