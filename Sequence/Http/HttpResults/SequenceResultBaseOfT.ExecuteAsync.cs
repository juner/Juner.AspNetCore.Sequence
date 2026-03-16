using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Threading.Channels;
using System.Diagnostics;
using System.Net.Http.Headers;


#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;
#endif                        

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

[DebuggerDisplay("{Values,nq}")]
public abstract partial class SequenceResultBase<T> : IResult
{
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
        return jsonOptions.SerializerOptions;

    }
    /// <inheritdoc/>
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Creating the logger with a string to preserve the category after the refactoring.
        var logger = GetLogger(httpContext.RequestServices);
        httpContext.Response.StatusCode = StatusCode;
        if (string.IsNullOrEmpty(httpContext.Response.ContentType))
            httpContext.Response.ContentType = ContentType;

        var serializerOptions = SequenceResultBase<T>.GetOptions(httpContext.RequestServices, logger);
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        var jsonTypeInfo = serializerOptions.GetTypeInfo<T>();
        if (_values is IAsyncEnumerable<T> asyncValues)
        {
            await InternalFormatWriter
                .WriteAsyncFromAsyncEnumerable(
                    values: asyncValues,
                    httpContext: httpContext,
                    SerializerOptions: serializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: Begin,
                    End: End,
                    SelectedEncoding: Encoding.UTF8,
                    logger: logger,
                    cancellationToken: httpContext.RequestAborted
                );
            return;
        }
        else if (_values is IEnumerable<T> values)
        {
            await InternalFormatWriter
                .WriteAsyncFromEnumerable(
                    values: values,
                    httpContext: httpContext,
                    SerializerOptions: serializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: Begin,
                    End: End,
                    SelectedEncoding: Encoding.UTF8,
                    logger: logger,
                    cancellationToken: httpContext.RequestAborted
                );
            return;
        }
        else if (_values is ChannelReader<T> channelReader)
        {
            await InternalFormatWriter
                .WriteAsyncFromChannelReader(
                    values: channelReader,
                    httpContext: httpContext,
                    SerializerOptions: serializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: Begin,
                    End: End,
                    SelectedEncoding: Encoding.UTF8,
                    logger: logger,
                    cancellationToken: httpContext.RequestAborted
                );
        }
    }
}