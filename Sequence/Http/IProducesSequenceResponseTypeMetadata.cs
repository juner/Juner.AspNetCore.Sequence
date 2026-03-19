using Microsoft.AspNetCore.Http.Metadata;

namespace Juner.AspNetCore.Sequence.Http;

public interface IProducesSequenceResponseTypeMetadata : IProducesResponseTypeMetadata
{
    /// <summary>
    /// Gets the optimistic sequence return type of the action.
    /// </summary>
    Type? ItemType { get; }

}