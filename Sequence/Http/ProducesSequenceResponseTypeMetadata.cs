using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Juner.AspNetCore.Sequence.Http;

public class ProducesSequenceResponseTypeMetadata : IProducesSequenceResponseTypeMetadata, IProducesResponseTypeMetadata
{
    public ProducesSequenceResponseTypeMetadata(int statusCode, Type? itemType = null, string[]? contentTypes = null)
    {
        StatusCode = statusCode;
        ItemType = itemType;

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
    public Type? ItemType { get; init; }

    Type? IProducesResponseTypeMetadata.Type => null;

    public int StatusCode { get; init; }

    public IEnumerable<string> ContentTypes { get; init; }
}