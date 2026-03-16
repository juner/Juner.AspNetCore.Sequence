using Juner.AspNetCore.Sequence.Http.HttpResults;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http;

public static class ResultsExtensions
{
    extension(Microsoft.AspNetCore.Http.Results)
    {
        public static JsonSequenceResult<T> JsonSequence<T>(IAsyncEnumerable<T> values) => SequenceResults.JsonSequence(values);
        public static JsonSequenceResult<T> JsonSequence<T>(IEnumerable<T> values) => SequenceResults.JsonSequence(values);
        public static JsonSequenceResult<T> JsonSequence<T>(ChannelReader<T> values) => SequenceResults.JsonSequence(values);
        public static JsonLineResult<T> JsonLine<T>(IAsyncEnumerable<T> values) => SequenceResults.JsonLine(values);
        public static JsonLineResult<T> JsonLine<T>(IEnumerable<T> values) => SequenceResults.JsonLine(values);
        public static JsonLineResult<T> JsonLine<T>(ChannelReader<T> values) => SequenceResults.JsonLine(values);
        public static NdJsonResult<T> NdJson<T>(IAsyncEnumerable<T> values) => SequenceResults.NdJson(values);
        public static NdJsonResult<T> NdJson<T>(IEnumerable<T> values) => SequenceResults.NdJson(values);
        public static NdJsonResult<T> NdJson<T>(ChannelReader<T> values) => SequenceResults.NdJson(values);
        public static SequenceResult<T> Sequence<T>(IAsyncEnumerable<T> values, string? contentType = null) => SequenceResults.Sequence(values, contentType);
        public static SequenceResult<T> Sequence<T>(IEnumerable<T> values, string? contentType = null) => SequenceResults.Sequence(values, contentType);
        public static SequenceResult<T> Sequence<T>(ChannelReader<T> values, string? contentType = null) => SequenceResults.Sequence(values, contentType);
    }
}