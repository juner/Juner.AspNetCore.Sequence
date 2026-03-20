using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using System.Reflection;
using System.Threading.Channels;

#if NET8_0_OR_GREATER
#endif

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

public sealed class NdJsonResult<T> : SequenceResultBase<T>, IEndpointMetadataProvider
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    public NdJsonResult(IEnumerable<T> values) : base(values) { }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    public NdJsonResult(IAsyncEnumerable<T> values) : base(values) { }

    public NdJsonResult(ChannelReader<T> values) : base(values) { }

    protected override ReadOnlyMemory<byte> Begin => default;

    #region LF
    static ReadOnlyMemory<byte>? _lf;
    static ReadOnlyMemory<byte> LF => _lf ??= "\n"u8.ToArray();
    #endregion
    protected override ReadOnlyMemory<byte> End => LF;

    #region StatusCode
    const int STATUS_CODE = StatusCodes.Status200OK;
    /// <summary>
    /// Gets the HTTP status code: <see cref="StatusCodes.Status200OK"/>
    /// </summary>

    public override int StatusCode => STATUS_CODE;
    #endregion

    #region ContentType
    const string CONTENT_TYPE =
        "application/x-ndjson";
    /// <summary>
    /// json-seq content type
    /// </summary>
    public override string ContentType => CONTENT_TYPE;
    #endregion

    /// <inheritdoc/>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(new ProducesSequenceResponseTypeMetadata(
            STATUS_CODE,
            typeof(T),
            [new Content(CONTENT_TYPE, true)]));
    }
}