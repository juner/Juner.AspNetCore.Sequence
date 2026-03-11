using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Mime;
using System.Reflection;
using System.Text;

namespace Juner.AspNetCore.Sequence.HttpResults;

public sealed class JsonSequence<T> : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<IAsyncEnumerable<T>>
{
    readonly IEnumerable<T> _values = null!;
    readonly IAsyncEnumerable<T> _asyncValues = null!;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    internal JsonSequence(IEnumerable<T> values) => _values = values;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    internal JsonSequence(IAsyncEnumerable<T> values) => _asyncValues = values;

    #region RS
    static ReadOnlyMemory<byte>? _rs;
    static ReadOnlyMemory<byte> RS => _rs ??= "\u001e"u8.ToArray();
    #endregion

    #region LF
    static ReadOnlyMemory<byte>? _lf;
    static ReadOnlyMemory<byte> LF => _lf ??= "\n"u8.ToArray();
    #endregion

    #region StatusCode
    const int STATUS_CODE = StatusCodes.Status200OK;
    /// <summary>
    /// Gets the HTTP status code: <see cref="StatusCodes.Status200OK"/>
    /// </summary>
    public int StatusCode => STATUS_CODE;
    int? IStatusCodeHttpResult.StatusCode => StatusCode;
    #endregion

    #region ContentType
    const string CONTENT_TYPE =
#if NET8_0_OR_GREATER
        MediaTypeNames.Application.JsonSequence;
#else
        "application/json-seq";
#endif
    /// <summary>
    /// json-seq content type
    /// </summary>
    public string ContentType => CONTENT_TYPE;
    #endregion

    #region Value    
    object? IValueHttpResult.Value => Value;

    IAsyncEnumerable<T>? IValueHttpResult<IAsyncEnumerable<T>>.Value => Value;

    /// <summary>
    /// Gets the object result.
    /// </summary>
    public IAsyncEnumerable<T> Value => _get_value ??= GetValue();

    IAsyncEnumerable<T>? _get_value = null;
    IAsyncEnumerable<T> GetValue()
    {
        if (_asyncValues is not null)
            return _asyncValues;
        if (_values is not null)
            return ToAsyncEnumerable(_values);
        return ToAsyncEnumerable([]);
        static async IAsyncEnumerable<T> ToAsyncEnumerable(IEnumerable<T> values)
        {
            await Task.Yield();
            foreach (var value in values)
                yield return value;
        }
    }
    #endregion

    /// <inheritdoc/>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Creating the logger with a string to preserve the category after the refactoring.
        var loggerFactory = httpContext.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Sequential.AspNetCore.Http.Result.JsonSequenceObjectResult");

        httpContext.Response.StatusCode = StatusCode;
        if (string.IsNullOrEmpty(httpContext.Response.ContentType))
            httpContext.Response.ContentType = ContentType;

        var SerializerOptions = (httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value ?? new JsonOptions()).SerializerOptions;
        var jsonTypeInfo = SerializerOptions.GetTypeInfo<T>();
        if (_asyncValues is not null)
            return InternalFormatWriter
                .WriteAsyncEnumerableAsync(
                    values: _asyncValues,
                    httpContext: httpContext,
                    SerializerOptions: SerializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: RS,
                    End: LF,
                    SelectedEncoding: Encoding.UTF8,
                    cancellationToken: httpContext.RequestAborted
                );
        if (_values is not null)
            return InternalFormatWriter
                .WriteEnumerableAsync(
                    values: _values,
                    httpContext: httpContext,
                    SerializerOptions: SerializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: RS,
                    End: LF,
                    SelectedEncoding: Encoding.UTF8,
                    cancellationToken: httpContext.RequestAborted

                );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            STATUS_CODE,
            typeof(IAsyncEnumerable<T>),
            [CONTENT_TYPE]));
    }
}
