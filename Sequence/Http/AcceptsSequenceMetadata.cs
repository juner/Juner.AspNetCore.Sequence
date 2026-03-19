using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Juner.AspNetCore.Sequence.Http;

public class AcceptsSequenceMetadata : IAcceptsSequenceMetadata, IAcceptsMetadata
{
    public AcceptsSequenceMetadata(Type? itemType = null, string[]? contentTypes = null, bool isOptional = false)
    {
        ItemType = itemType;
        IsOptional = isOptional;

        if (contentTypes is null || contentTypes.Length == 0)
        {
            ContentTypes = [];
        }
        else
        {
            for (var i = 0; i < contentTypes.Length; i++)
            {
                MediaTypeHeaderValue.Parse(contentTypes[i]);
                ValidateContentType(contentTypes[i]);
            }

            ContentTypes = contentTypes;
        }

        static void ValidateContentType(string type)
        {
            if (type.Contains('*', StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Could not parse '{type}'. Content types with wildcards are not supported.");
            }
        }
    }
    public IReadOnlyList<string> ContentTypes { get; init; }

    public Type? RequestType => typeof(Stream);

    public bool IsOptional { get; init; }

    public Type? ItemType { get; init; }
}