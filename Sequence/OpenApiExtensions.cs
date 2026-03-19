#if NET10_0_OR_GREATER
using Juner.AspNetCore.Sequence.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

#pragma warning disable IDE0130 // Namespace がフォルダー構造と一致しません
namespace Microsoft.Extensions.DependencyInjection;

public static class OpenApiExtensions
{

    public static IServiceCollection AddSequenceOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(
            options => options.AddOperationTransformer<SequenceOpenApiTransformer>()
        );
        return services;
    }
}
public class SequenceOpenApiTransformer: IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var accepts1 = metadata
            .OfType<IAcceptsSequenceMetadata>()
            .FirstOrDefault();

        if (accepts1 != null)
        {
            operation.RequestBody
                = await CreateRequestBody(operation.RequestBody, accepts1, context, cancellationToken);
        }

        var produces1 = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IProducesSequenceResponseTypeMetadata>()
            .ToArray();

        if (produces1 is { Length:>0})
        {
            operation.Responses
                = await CreateResponses(operation.Responses, produces1, context, cancellationToken);
        }

    }

    static async ValueTask<IOpenApiRequestBody?> CreateRequestBody(IOpenApiRequestBody? requestBody,
        IAcceptsSequenceMetadata sequenceMetadata, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {

        if (sequenceMetadata.ItemType is null) return requestBody;
        var schema = await context.GetOrCreateSchemaAsync(sequenceMetadata.ItemType, cancellationToken: cancellationToken);
        if (schema is not { Type: { } type, Format: { } format }) return requestBody;
        if (requestBody is not { Content: { } content }) return requestBody;
        JsonNode typeNode;
        {
            await using var stream = new MemoryStream();
            await schema.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_1, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            typeNode = JsonNode.Parse(stream)!;
        }
        foreach (var kv in content)
        {
            var mediaType = kv.Value;

            var extensions = mediaType.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            extensions.TryAdd("x-streaming", new JsonNodeExtension(JsonValue.Create(true)));
            extensions.TryAdd("x-itemSchema", new JsonNodeExtension(typeNode));
        }
        return requestBody;

    }
    static async ValueTask<OpenApiResponses?> CreateResponses(OpenApiResponses? responses, IProducesSequenceResponseTypeMetadata[] sequenceMetadata, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach(var metadata in sequenceMetadata)
        {
            responses ??= new OpenApiResponses();
            var statusCode = $"{metadata.StatusCode}";
            if (!responses.TryGetValue(statusCode, out var response))
            {
                 response = new OpenApiResponse();
                responses[statusCode] = response;
            }
            metadata.ContentTypes
            metadata.StatusCode
        }
    }
}
#endif

