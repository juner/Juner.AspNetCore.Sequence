using Microsoft.AspNetCore.Http.Metadata;

namespace Juner.AspNetCore.Sequence.Http;

/// <summary>
/// 
/// </summary>
public interface IAcceptsSequenceMetadata : IAcceptsMetadata
{
    /// <summary>
    /// 
    /// </summary>
    new IReadOnlyList<IContent> ContentTypes { get; }

    /// <summary>
    /// 
    /// </summary>
    Type? ItemType { get; }

}