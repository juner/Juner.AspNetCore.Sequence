#if !NET8_0_OR_GREATER
using Microsoft.AspNetCore.Http.Metadata;
using System.Diagnostics;

#pragma warning disable IDE0130 // Namespace がフォルダー構造と一致しません
namespace Microsoft.AspNetCore.Http;
#pragma warning restore IDE0130 // Namespace がフォルダー構造と一致しません


/// <summary>
/// Specifies the type of the value and status code returned by the action.
/// </summary>
/// <param name="statusCode">The HTTP response status code.</param>
/// <param name="type">The Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata.Type of object that is going to be written in the response.</param>
/// <param name="contentTypes">Content types supported by the response.</param>
[DebuggerDisplay("{ToString(),nq}")]
internal sealed class ProducesResponseTypeMetadata(int statusCode, Type? type = null, string[]? contentTypes = null) : IProducesResponseTypeMetadata
{

    /// <inheritdoc/>
    public Type? Type { get; } = type;
    
    /// <inheritdoc/>
    public int StatusCode { get; } = statusCode;
    
    /// <inheritdoc/>
    public string? Description { get; set; }
    
    /// <inheritdoc/>
    public IEnumerable<string> ContentTypes { get; } = contentTypes ?? Enumerable.Empty<string>();

    public override string ToString() => $"{nameof(ProducesResponseTypeMetadata)} {{ {nameof(Type)}:{Type}, {nameof(StatusCode)}:{StatusCode}, {nameof(Description)}:{Description}, {nameof(ContentTypes)}:{string.Join(", ", ContentTypes ?? Enumerable.Empty<string>())} }}"; 
}
#endif