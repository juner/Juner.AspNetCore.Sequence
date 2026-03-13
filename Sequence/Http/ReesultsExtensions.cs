using Juner.AspNetCore.Sequence.Http.HttpResults;

namespace Juner.AspNetCore.Sequence.Http;

public static class ResultsExtensions
{
    extension(Microsoft.AspNetCore.Http.Results)
    {
        public static JsonSequenceResult<T> JsonSequence<T>(IAsyncEnumerable<T> values) => new(values);
        public static JsonSequenceResult<T> JsonSequence<T>(IEnumerable<T> values) => new(values);
    }
}
