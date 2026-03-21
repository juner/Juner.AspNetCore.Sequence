using Juner.AspNetCore.Sequence.Http.HttpResults;
using Microsoft.AspNetCore.Http;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http;

public static class ResultsExtensions
{
    extension(Microsoft.AspNetCore.Http.Results)
    {
        public static IResult JsonSequence<T>(IAsyncEnumerable<T> values) => SequenceResults.JsonSequence(values);
        public static IResult JsonSequence<T>(IEnumerable<T> values) => SequenceResults.JsonSequence(values);
        public static IResult JsonSequence<T>(ChannelReader<T> values) => SequenceResults.JsonSequence(values);
        public static IResult JsonLine<T>(IAsyncEnumerable<T> values) => SequenceResults.JsonLine(values);
        public static IResult JsonLine<T>(IEnumerable<T> values) => SequenceResults.JsonLine(values);
        public static IResult JsonLine<T>(ChannelReader<T> values) => SequenceResults.JsonLine(values);
        public static IResult NdJson<T>(IAsyncEnumerable<T> values) => SequenceResults.NdJson(values);
        public static IResult NdJson<T>(IEnumerable<T> values) => SequenceResults.NdJson(values);
        public static IResult NdJson<T>(ChannelReader<T> values) => SequenceResults.NdJson(values);
        public static IResult Sequence<T>(IAsyncEnumerable<T> values, string? contentType = null) => SequenceResults.Sequence(values, contentType);
        public static IResult Sequence<T>(IEnumerable<T> values, string? contentType = null) => SequenceResults.Sequence(values, contentType);
        public static IResult Sequence<T>(ChannelReader<T> values, string? contentType = null) => SequenceResults.Sequence(values, contentType);
    }
}