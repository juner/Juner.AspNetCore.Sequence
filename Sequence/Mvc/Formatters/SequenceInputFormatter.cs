using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Mvc.Formatters;

public partial class SequenceInputFormatter : TextInputFormatter
{
    public JsonSerializerOptions SerializerOptions { get; }

    const string ContentType =
#if NET8_0_OR_GREATER
        System.Net.Mime.MediaTypeNames.Application.JsonSequence;
#else
        "application/json-seq";
#endif

    public SequenceInputFormatter(JsonSerializerOptions options)
    {
        SerializerOptions = options;

        SupportedMediaTypes.Add(ContentType);
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanReadType(Type type)
        => TryGetElementType(type, out _, out _);

    public override bool CanRead(InputFormatterContext context)
    {
        if (!base.CanRead(context))
            return false;

        if (!TryGetElementType(context.ModelType, out _,  out _))
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
        var jsonSerializerOptions = GetOptions(httpContext.RequestServices, logger);

        if (!TryGetElementType(context.ModelType, out var enumerableType, out var elementType))
            return await InputFormatterResult.FailureAsync();

        var method = GetType()
            .GetMethod(nameof(ReadAsyncEnumerable), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(elementType);

        var result = method.Invoke(this,
        [
            request,
            context.HttpContext.RequestAborted
        ]);
        if (result is Task taskResult)
            return await InputFormatterResult.SuccessAsync(await (Task<object>)taskResult);

        return await InputFormatterResult.SuccessAsync(result);
    }

    async IAsyncEnumerable<T> ReadAsyncEnumerable<T>(
        HttpRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body);

        int prefix;
        while ((prefix = reader.Read()) >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (prefix != 0x1E) // RS
                continue;

            var json = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
                continue;

            var value = JsonSerializer.Deserialize<T>(json, SerializerOptions);

            if (value != null)
                yield return value;
        }
    }

    static bool TryGetElementType(Type type,out EnumerableType enumerableType, [NotNullWhen(true)] out Type? elementType)
    {
        elementType = null;
        enumerableType = default;

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();

            if (def == typeof(IAsyncEnumerable<>)
             || def == typeof(IEnumerable<>)
             || def == typeof(ChannelReader<>)
             || def == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                enumerableType =
                    def == typeof(IEnumerable<>) ? EnumerableType.Enumerable
                    : def == typeof(ChannelReader<>) ? EnumerableType.ChannelReader
                    : def == typeof(List<>) ? EnumerableType.List
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
    static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "not register IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>"
        )]
        public static partial void LogNotHaveJsonOptions(ILogger logger);
    }

}