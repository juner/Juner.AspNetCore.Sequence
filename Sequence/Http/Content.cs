namespace Juner.AspNetCore.Sequence.Http;

/// <summary>
/// 
/// </summary>
/// <param name="ContentType"></param>
/// <param name="IsStreaming"></param>
public record Content(string ContentType, bool IsStreaming) : IContent;