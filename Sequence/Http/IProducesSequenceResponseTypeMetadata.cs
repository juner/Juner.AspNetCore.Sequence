namespace Juner.AspNetCore.Sequence.Http;

public interface IProducesSequenceResponseTypeMetadata
{
    /// <summary>
    /// Gets the optimistic sequence return type of the action.
    /// </summary>
    Type? ItemType { get; }

}