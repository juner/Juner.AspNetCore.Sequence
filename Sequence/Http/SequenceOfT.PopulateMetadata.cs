using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using System.Reflection;

namespace Juner.AspNetCore.Sequence.Http;
public sealed partial class Sequence<T> : IEndpointParameterMetadataProvider
{
    public static void PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
        => builder.Metadata.Add(new AcceptsSequenceMetadata(
            typeof(T),
            [.. MakePatternActionList.Select(v => v.ContentType)],
            false
        ));

}