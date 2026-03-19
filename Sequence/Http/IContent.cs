namespace Juner.AspNetCore.Sequence.Http;

public interface IContent
{
    string ContentType { get; }
    bool IsStreaming { get; }
}