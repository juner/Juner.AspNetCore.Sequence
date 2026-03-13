using Juner.AspNetCore.Sequence.Http.HttpResults;

namespace Juner.AspNetCore.Sequence.Http;

public static class TypedResultsExtensions
{
    extension(Microsoft.AspNetCore.Http.TypedResults)
    {
        public static JsonSequenceResult<T> JsonSequence<T>(IAsyncEnumerable<T> values) => new(values);
        public static JsonSequenceResult<T> JsonSequence<T>(IEnumerable<T> values) => new(values);
    }
}
