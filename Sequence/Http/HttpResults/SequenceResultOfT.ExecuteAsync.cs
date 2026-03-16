using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Net.Mime;
using Microsoft.Net.Http.Headers;





#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;
#endif                        

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

[DebuggerDisplay("{Values,nq}")]
public partial class SequenceResult<T> : IResult
{
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
        return jsonOptions.SerializerOptions;

    }
    /// <inheritdoc/>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var logger = GetLogger(httpContext.RequestServices);

        httpContext.Response.StatusCode = StatusCode;

        if (!TrySelectPattern(
            httpContext,
            _contentType,
            out var contentType,
            out var begin,
            out var end))
            throw new InvalidOperationException();

        httpContext.Response.ContentType = contentType;

        var serializerOptions = GetOptions(httpContext.RequestServices, logger);

        var values = ToAsyncEnumerable(httpContext.RequestAborted);

        if (contentType == MediaTypeNames.Application.Json)
        {
            var jsonTypeInfo = serializerOptions.GetTypeInfo<IAsyncEnumerable<T>>();

            await JsonSerializer.SerializeAsync(
                httpContext.Response.Body,
                values,
                jsonTypeInfo,
                httpContext.RequestAborted);

            return;
        }

        var elementTypeInfo = serializerOptions.GetTypeInfo<T>();

        await InternalFormatWriter.WriteAsyncFromAsyncEnumerable(
            values,
            httpContext,  
            elementTypeInfo,
            serializerOptions,  
            Encoding.UTF8,
            logger,
            begin,
            end,
            httpContext.RequestAborted);
    }

}