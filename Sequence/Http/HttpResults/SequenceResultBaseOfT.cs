using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

[DebuggerDisplay("{Values,nq}")]
public abstract partial class SequenceResultBase<T> : IStatusCodeHttpResult, ISequenceHttpResult, ISequenceHttpResult<T>
{
    readonly object? _values = null;

    internal object? Values => _values;

    internal async IAsyncEnumerable<T> ToAsyncEnumerable([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_values is IAsyncEnumerable<T> asyncEnumerable)
        {
            await foreach (var item in asyncEnumerable)
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
    /// <param name="values"></param>
    public SequenceResultBase(IEnumerable<T> values) => _values = values;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    public SequenceResultBase(IAsyncEnumerable<T> values) => _values = values;

    public SequenceResultBase(ChannelReader<T> values) => _values = values;

    protected abstract ReadOnlyMemory<byte> Begin { get; }

    protected abstract ReadOnlyMemory<byte> End { get; }

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public abstract int StatusCode { get; }

    public abstract string ContentType { get; }

    static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "not register IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>"
        )]
        public static partial void LogNotHaveJsonOptions(ILogger logger);
    }
}