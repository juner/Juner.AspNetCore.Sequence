#if NET10_0_OR_GREATER
using Juner.AspNetCore.Sequence.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System.Reflection.Metadata;
using System.Text.Json.Nodes;

#pragma warning disable IDE0130 // Namespace がフォルダー構造と一致しません
namespace Microsoft.Extensions.DependencyInjection;

public static class OpenApiExtensions
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
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
        var openApiVersion = context.ApplicationServices.GetRequiredService<IOptions<OpenApiOptions>>().Value.OpenApiVersion;

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var accepts1 = metadata
            .OfType<IAcceptsSequenceMetadata>()
            .FirstOrDefault();

        if (accepts1 != null)
        {
            operation.RequestBody
                = await CreateRequestBody(operation.RequestBody, accepts1, context, openApiVersion, cancellationToken);
        }

        var produces1 = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IProducesSequenceResponseTypeMetadata>()
            .ToArray();

        if (produces1 is { Length:>0})
        {
            operation.Responses
                = await CreateResponses(operation.Responses, produces1, context, openApiVersion, cancellationToken);
        }

    }

    static async ValueTask<IOpenApiRequestBody?> CreateRequestBody(IOpenApiRequestBody? requestBody,
        IAcceptsSequenceMetadata sequenceMetadata, OpenApiOperationTransformerContext context, OpenApiSpecVersion openApiVersion, CancellationToken cancellationToken)
    {
        if (sequenceMetadata.ItemType is null) return requestBody;
        var schema = await context.GetOrCreateSchemaAsync(sequenceMetadata.ItemType, cancellationToken: cancellationToken);
        if (schema is not { Type: { } type, Format: { } format }) return requestBody;
        if (requestBody is not { Content: { } content }) return requestBody;
        JsonNode typeNode;
        {
            await using var stream = new MemoryStream();
            await schema.SerializeAsJsonAsync(stream, openApiVersion, cancellationToken);
            stream.Seek(0, SeekOrigin.Begin);
            typeNode = JsonNode.Parse(stream)!;
        }
        foreach (var contentType_ in sequenceMetadata.ContentTypes)
        {
            var contentType = contentType_.ContentType;
            var isStreaming = contentType_.IsStreaming;
            if (string.IsNullOrEmpty(contentType)) continue;
            if (!content.TryGetValue(contentType, out var mediaType))
            {
                mediaType = new OpenApiMediaType();
                content[contentType] = mediaType;
            }
            if (isStreaming)
            {
                mediaType.Schema ??= (schema ??= await context.GetOrCreateSchemaAsync(typeof(Stream), cancellationToken: cancellationToken));
                
                var extensions = mediaType.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                extensions.TryAdd("x-streaming", new JsonNodeExtension(JsonValue.Create(true)));
                extensions.TryAdd("x-itemSchema", new JsonNodeExtension(typeNode.DeepClone()));
            } else
            {
                mediaType.Schema = await context.GetOrCreateSchemaAsync(typeof(IAsyncEnumerable<>).MakeGenericType(sequenceMetadata.ItemType), cancellationToken: cancellationToken);
            }
        }
        return requestBody;

    }
    static async ValueTask<OpenApiResponses?> CreateResponses(OpenApiResponses? responses, IProducesSequenceResponseTypeMetadata[] sequenceMetadata, OpenApiOperationTransformerContext context, OpenApiSpecVersion openApiVersion, CancellationToken cancellationToken)
    {
        OpenApiSchema? schema = null;
        foreach(var metadata in sequenceMetadata)
        {
            if (!metadata.ContentTypes.Any()) continue;
            if (metadata.ItemType is null) continue;
            var itemSchema = await context.GetOrCreateSchemaAsync(metadata.ItemType, cancellationToken: cancellationToken);
            JsonNode itemSchemaJsonNode;
            {
                await using var stream = new MemoryStream();
                await itemSchema.SerializeAsJsonAsync(stream, openApiVersion, cancellationToken);
                stream.Seek(0, SeekOrigin.Begin);
                itemSchemaJsonNode = JsonNode.Parse(stream)!;
            }
            responses ??= [];
            var statusCode = $"{metadata.StatusCode}";
            OpenApiResponse response;
            if (!responses.TryGetValue(statusCode, out var response2))
            {
                response = new OpenApiResponse();
                responses[statusCode] = response;
            } else if (response2 is OpenApiResponse response3)
            {
                response = response3;
            } else {
                response = new OpenApiResponse
                {
                    Content = response2.Content,
                    Description = response2.Description,
                    Extensions = response2.Extensions,
                    Headers = response2.Headers,
                    Links = response2.Links,
                };
            }
            foreach(var contentType_ in metadata.ContentTypes)
            {
                var contentType = contentType_.ContentType;
                var isStreaming = contentType_.IsStreaming;
                if (!(response.Content ??= new Dictionary<string, OpenApiMediaType>()).TryGetValue(contentType, out var schema_))
                {
                    schema_ = new OpenApiMediaType();
                    response.Content.Add(contentType, schema_);
                }

                if (isStreaming)
                {
                    schema_.Schema ??= (schema ??= await context.GetOrCreateSchemaAsync(typeof(Stream), cancellationToken: cancellationToken));

                    var extensions = schema_.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                    extensions.TryAdd("x-streaming", new JsonNodeExtension(JsonValue.Create(true)));
                    extensions.TryAdd("x-itemSchema", new JsonNodeExtension(itemSchemaJsonNode.DeepClone()));
                }
                else
                {
                    schema_.Schema = await context.GetOrCreateSchemaAsync(typeof(IAsyncEnumerable<>).MakeGenericType(metadata.ItemType), cancellationToken: cancellationToken);
                }
            }
        }
        return responses;
    }
}
#endif