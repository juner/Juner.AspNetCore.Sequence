using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Net.Mime;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Juner.AspNetCore.Sequence.Formatters;

/// <summary>
/// application/json-seq 対応の フォーマッター
/// </summary>
public class JsonSequenceOutputFormatter : TextOutputFormatter
{
    /// <summary>
    /// 
    /// </summary>
    public JsonSerializerOptions SerializerOptions { get; }

    /// <summary>
    /// 
    /// </summary>
    public JsonSequenceOutputFormatter(JsonSerializerOptions jsonSerializerOptions)
    {
        SerializerOptions = jsonSerializerOptions;
#if NET8_0_OR_GREATER
        jsonSerializerOptions.MakeReadOnly();
#endif
        SupportedMediaTypes.Add(ContentType);
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    internal static JsonSequenceOutputFormatter CreateFormatter(JsonOptions jsonOptions)
    {
        var jsonSerializerOptions = jsonOptions.JsonSerializerOptions;

        if (jsonSerializerOptions.Encoder is null)
        {
            // If the user hasn't explicitly configured the encoder, use the less strict encoder that does not encode all non-ASCII characters.
            jsonSerializerOptions = new JsonSerializerOptions(jsonSerializerOptions)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
        }

        return new JsonSequenceOutputFormatter(jsonSerializerOptions);
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

        await new InternalSequenceOutputFormatWriter
        {
            Begin = RS,
            End = LF,
            SerializerOptions = SerializerOptions,
        }.WriteResponseBodyAsync(context, selectedEncoding);
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
