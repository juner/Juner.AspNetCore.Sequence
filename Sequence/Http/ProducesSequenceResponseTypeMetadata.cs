using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;

namespace Juner.AspNetCore.Sequence.Http;

public class ProducesSequenceResponseTypeMetadata : IProducesSequenceResponseTypeMetadata, IProducesResponseTypeMetadata
{
    public ProducesSequenceResponseTypeMetadata(int statusCode, Type? itemType = null, IContent[]? contentTypes = null)
    {
        StatusCode = statusCode;
        ItemType = itemType;

        if (contentTypes is null || contentTypes.Length == 0)
        {
            ContentTypes = [];
            OnlyContentTypes = [];
        }
        else
        {
            for (var i = 0; i < contentTypes.Length; i++)
            {
                MediaTypeHeaderValue.Parse(contentTypes[i].ContentType);
                ValidateContentType(contentTypes[i].ContentType);
            }

            ContentTypes = contentTypes;
            OnlyContentTypes = [.. contentTypes.Select(v => v.ContentType)];
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

    Type? IProducesResponseTypeMetadata.Type => typeof(Stream);

    public int StatusCode { get; init; }

    public IReadOnlyList<IContent> ContentTypes { get; init; }

    IEnumerable<string> IProducesResponseTypeMetadata.ContentTypes => OnlyContentTypes;

    IReadOnlyList<string> OnlyContentTypes { get; init; }
}