using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Mime;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Juner.AspNetCore.Sequence.Formatters;

/// <summary>
/// application/json-seq 対応の フォーマッター
/// </summary>
public class JsonSequenceOutputFormatter : TextOutputFormatter
{
    /// <summary>
    /// 
    /// </summary>
    public JsonSequenceOutputFormatter()
    {
        SupportedMediaTypes.Add(ContentType);
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    const string ContentType =
#if NET8_0_OR_GREATER
        MediaTypeNames.Application.JsonSequence;
#else
        "application/json-seq";
#endif

    /// <inheritdoc />
    protected override bool CanWriteType(Type? type)
      => InternalFormatWriter.TryGetOutputMode(type, out _, out _);

    /// <inheritdoc/>
    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        if (!InternalFormatWriter.TryGetOutputMode(context.ObjectType, out _, out _))
            return false;

        var accept = context.HttpContext.Request.GetTypedHeaders().Accept;

        if (accept == null || accept.Count == 0)
            return false;

        return accept.Any(v => v.MediaType == ContentType);
    }

    /// <inheritdoc cref="TextOutputFormatter.WriteResponseBodyAsync(OutputFormatterWriteContext, Encoding)" />
    public sealed override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedEncoding);

        var cancellationToken = context.HttpContext.RequestAborted;
        var jsonSerializerOptions = (context.HttpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value ?? new JsonOptions()).JsonSerializerOptions;
#if !NET8_0_OR_GREATER
        jsonSerializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        await InternalFormatWriter.Create(
            serializerOptions: jsonSerializerOptions,
            begin: RS,
            end: LF,
            context: context,
            selectedEncoding: selectedEncoding
        ).WriteResponseBodyAsync(cancellationToken);
    }

    #region RS
    static ReadOnlyMemory<byte>? _rs;
    static ReadOnlyMemory<byte> RS => _rs ??= "\u001e"u8.ToArray();
    #endregion

    #region LF
    static ReadOnlyMemory<byte>? _lf;
    static ReadOnlyMemory<byte> LF => _lf ??= "\n"u8.ToArray();
    #endregion


}