using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

#if NET8_0_OR_GREATER
using System.Net.Mime;
#else                         
using System.Text.Json.Serialization.Metadata;
#endif

namespace Juner.AspNetCore.Sequence.Formatters;

/// <summary>
/// application/json-seq 対応の フォーマッター
/// </summary>
public partial class JsonSequenceOutputFormatter : TextOutputFormatter
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

    ILogger GetLogger(IServiceProvider provider)
    {
        var loggerType = typeof(ILogger<>).MakeGenericType(GetType());
        var logger = provider.GetService(loggerType) as ILogger;
        if (logger is not null) return logger;
        return NullLogger.Instance;
    }
    JsonSerializerOptions GetOptions(IServiceProvider provider, ILogger logger)
    {
        var jsonOptions = provider.GetService<IOptions<JsonOptions>>()?.Value;
        if (jsonOptions is null)
        {
            Log.LogNotHaveJsonOptions(logger);
            jsonOptions = new JsonOptions();
        }
        return jsonOptions.JsonSerializerOptions;

    }

    /// <inheritdoc cref="TextOutputFormatter.WriteResponseBodyAsync(OutputFormatterWriteContext, Encoding)" />
    public sealed override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedEncoding);

        var cancellationToken = context.HttpContext.RequestAborted;
        var logger = GetLogger(context.HttpContext.RequestServices);
        var serializerOptions = GetOptions(context.HttpContext.RequestServices, logger);
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        await InternalFormatWriter.Create(
            serializerOptions: serializerOptions,
            begin: RS,
            end: LF,
            context: context,
            selectedEncoding: selectedEncoding,
            logger: logger
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
    static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "not register IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>"
        )]
        public static partial void LogNotHaveJsonOptions(ILogger logger);
    }
}