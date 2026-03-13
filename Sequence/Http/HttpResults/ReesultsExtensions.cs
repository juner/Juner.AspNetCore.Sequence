namespace Juner.AspNetCore.Sequence.Http.HttpResults;

public static class ResultsExtensions
{
    extension(Microsoft.AspNetCore.Http.Results)
    {
        public static JsonSequence<T> JsonSequence<T>(IAsyncEnumerable<T> values) => new(values);
        public static JsonSequence<T> JsonSequence<T>(IEnumerable<T> values) => new(values);
    }
}
