using Microsoft.AspNetCore.Http.Metadata;

namespace Juner.AspNetCore.Sequence.Http;

public interface IAcceptsSequenceMetadata : IAcceptsMetadata
{
    Type? ItemType { get; }
}
