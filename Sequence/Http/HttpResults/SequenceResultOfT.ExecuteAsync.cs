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
        ArgumentNullException.ThrowIfNull(httpContext);

        // Creating the logger with a string to preserve the category after the refactoring.
        var logger = GetLogger(httpContext.RequestServices);

        httpContext.Response.StatusCode = StatusCode;
        string? contentType = null;
        ReadOnlyMemory<byte> begin = default;
        ReadOnlyMemory<byte> end = default;
        {
            var accepts = MediaTypeHeaderValue.ParseList(httpContext.Request.Headers.Accept);

            if (accepts.Count > 0)
            {
                foreach (var accept in accepts)
                {
                    if (TryGetPattern(accept.MediaType.ToString(), out begin, out end))
                    {
                        contentType = accept.MediaType.ToString();
                        break;
                    }
                }
                if (string.IsNullOrEmpty(contentType))
                    throw new InvalidOperationException($"not support accept:{httpContext.Request.Headers.Accept}");
            }
            contentType ??= _contentType;
        }
        if (string.IsNullOrEmpty(contentType))
            throw new InvalidOperationException($"not have contentType target");
        if (string.IsNullOrEmpty(httpContext.Response.ContentType))
            httpContext.Response.ContentType = contentType;

        var serializerOptions = GetOptions(httpContext.RequestServices, logger);
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        if (contentType.Equals(MediaTypeNames.Application.Json, StringComparison.OrdinalIgnoreCase))
        {
            var jsonTypeInfo2 = serializerOptions.GetTypeInfo<IAsyncEnumerable<T>>();
            await JsonSerializer.SerializeAsync(utf8Json: httpContext.Response.Body, value: ToAsyncEnumerable(httpContext.RequestAborted), jsonTypeInfo: jsonTypeInfo2, httpContext.RequestAborted);
            return;
        }
        var jsonTypeInfo = serializerOptions.GetTypeInfo<T>();
        if (_values is IAsyncEnumerable<T> asyncValues)
        {
            await InternalFormatWriter
                .WriteAsyncFromAsyncEnumerable(
                    values: asyncValues,
                    httpContext: httpContext,
                    SerializerOptions: serializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: begin,
                    End: end,
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
                    Begin: begin,
                    End: end,
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
                    Begin: begin,
                    End: end,
                    SelectedEncoding: Encoding.UTF8,
                    logger: logger,
                    cancellationToken: httpContext.RequestAborted
                );
        }
    }
}