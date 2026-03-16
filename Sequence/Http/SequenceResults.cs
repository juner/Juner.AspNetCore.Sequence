using Juner.AspNetCore.Sequence.Http.HttpResults;
using System.Net.Mime;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http;

public static class SequenceResults
{
    public static JsonSequenceResult<T> JsonSequence<T>(IAsyncEnumerable<T> values) => new(values);
    public static JsonSequenceResult<T> JsonSequence<T>(IEnumerable<T> values) => new(values);
    public static JsonSequenceResult<T> JsonSequence<T>(ChannelReader<T> values) => new(values);
    public static JsonLineResult<T> JsonLine<T>(IAsyncEnumerable<T> values) => new(values);
    public static JsonLineResult<T> JsonLine<T>(IEnumerable<T> values) => new(values);
    public static JsonLineResult<T> JsonLine<T>(ChannelReader<T> values) => new(values);
    public static NdJsonResult<T> NdJson<T>(IAsyncEnumerable<T> values) => new(values);
    public static NdJsonResult<T> NdJson<T>(IEnumerable<T> values) => new(values);
    public static NdJsonResult<T> NdJson<T>(ChannelReader<T> values) => new(values);
    public static SequenceResult<T> Sequence<T>(IAsyncEnumerable<T> values, string? contentType = null) => new(values, contentType);
    public static SequenceResult<T> Sequence<T>(IEnumerable<T> values, string? contentType = null) => new(values, contentType);
    public static SequenceResult<T> Sequence<T>(ChannelReader<T> values, string? contentType = null) => new(values, contentType);

}