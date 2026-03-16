using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Mvc.Formatters;

public partial class SequenceInputFormatter : TextInputFormatter
{

    const string ContentTypeJsonSequence =
#if NET8_0_OR_GREATER
        System.Net.Mime.MediaTypeNames.Application.JsonSequence;
#else
        "application/json-seq";
#endif
    const string ContentTypeNdJson =
        "application/x-ndjson";
    const string ContentTypeJsonLine =
        "application/jsonl";

    public SequenceInputFormatter()
    {
        SupportedMediaTypes.Add(ContentTypeJsonSequence);
        SupportedMediaTypes.Add(ContentTypeNdJson);
        SupportedMediaTypes.Add(ContentTypeJsonLine);
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }


    protected override bool CanReadType(Type type)
        => TryGetElementType(type, out _, out _);

    public override bool CanRead(InputFormatterContext context)
    {
        if (!base.CanRead(context))
            return false;

        if (!TryGetElementType(context.ModelType, out _, out _))
            return false;
        if (!MediaTypeHeaderValue.TryParse(context.HttpContext.Request.ContentType!, out var parsedValue))
            return false;
        var mediaType = parsedValue.MediaType;
        if (!TryGetSequenceType(mediaType, out _, out _))
            return false;
        return true;
    }

    ILogger GetLogger(IServiceProvider provider)
    {
        var loggerType = typeof(ILogger<>).MakeGenericType(GetType());
        var logger = provider.GetService(loggerType) as ILogger;
        if (logger is not null) return logger;
        return NullLogger.Instance;
    }
    static JsonSerializerOptions GetOptions(IServiceProvider provider, ILogger logger)
    {
        var jsonOptions = provider.GetService<IOptions<JsonOptions>>()?.Value;
        if (jsonOptions is null)
        {
            Log.LogNotHaveJsonOptions(logger);
            jsonOptions = new JsonOptions();
        }
        return jsonOptions.JsonSerializerOptions;
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context,
        Encoding encoding)
    {
        var request = context.HttpContext.Request;
        var cancellationToken = context.HttpContext.RequestAborted;
        var httpContext = context.HttpContext;
        var logger = GetLogger(httpContext.RequestServices);
        var serializerOptions = GetOptions(httpContext.RequestServices, logger);
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif

        if (!TryGetElementType(context.ModelType, out var enumerableType, out var elementType))
            return await InputFormatterResult.FailureAsync();


        if (string.IsNullOrEmpty(context.HttpContext.Request.ContentType) || !MediaTypeHeaderValue.TryParse(context.HttpContext.Request.ContentType, out var parsedValue))
            return await InputFormatterResult.FailureAsync();

        var mediaType = parsedValue.MediaType;
        if (!TryGetSequenceType(mediaType, out var start, out var end))
            return await InputFormatterResult.FailureAsync();

        var jsonTypeInfo = serializerOptions.GetTypeInfo(elementType);

        var result = InternalFormatReader.ReadResult(
            elementType,
            enumerableType,
            request.BodyReader,
            jsonTypeInfo,
            start,
            end,
            cancellationToken);

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            result = task.GetType().GetProperty("Result")!.GetValue(task);
        }

        return await InputFormatterResult.SuccessAsync(result);
    }

    static bool TryGetSequenceType(StringSegment mediaType, [NotNullWhen(true)] out ReadOnlyMemory<byte>[] start, [NotNullWhen(true)] out ReadOnlyMemory<byte>[] end)
    {
        start = default!;
        end = default!;
        if (mediaType.Equals(ContentTypeJsonSequence, StringComparison.OrdinalIgnoreCase))
        {
            start = JSONSEQ_START;
            end = JSONSEQ_END;
            return true;
        }
        if (mediaType.Equals(ContentTypeNdJson, StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals(ContentTypeJsonLine, StringComparison.OrdinalIgnoreCase))
        {
            start = NDJSON_START;
            end = NDJSON_END;
            return true;
        }
        return false;
    }

    static bool TryGetElementType(Type type, out EnumerableType enumerableType, [NotNullWhen(true)] out Type? elementType)
    {
        elementType = null;
        enumerableType = default;

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();

            if (def == typeof(IAsyncEnumerable<>)
             || def == typeof(IEnumerable<>)
             || def == typeof(ChannelReader<>)
             || def == typeof(List<>)
             || def == typeof(Http.Sequence<>))
            {
                elementType = type.GetGenericArguments()[0];
                enumerableType =
                    def == typeof(IEnumerable<>) ? EnumerableType.Enumerable
                    : def == typeof(ChannelReader<>) ? EnumerableType.ChannelReader
                    : def == typeof(List<>) ? EnumerableType.List
                    : def == typeof(Http.Sequence<>) ? EnumerableType.Sequence
                    : EnumerableType.AsyncEnumerable;
                return true;
            }
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType();
            enumerableType = EnumerableType.Array;
            return elementType != null;
        }

        return false;
    }

    #region delimiters

    static readonly byte[] RS = [.. "\u001e"u8];
    static readonly byte[] LF = [.. "\n"u8];

    static readonly ReadOnlyMemory<byte>[] JSONSEQ_START = [RS];
    static readonly ReadOnlyMemory<byte>[] JSONSEQ_END = [LF];

    static readonly ReadOnlyMemory<byte>[] NDJSON_START = [];
    static readonly ReadOnlyMemory<byte>[] NDJSON_END = [LF];

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