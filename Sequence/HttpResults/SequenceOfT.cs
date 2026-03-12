using Juner.AspNetCore.Sequence.Internals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
#if !NET8_0_OR_GREATER
using System.Text.Json.Serialization.Metadata;
#endif

namespace Juner.AspNetCore.Sequence.HttpResults;

public abstract class Sequence<T> : IResult, IStatusCodeHttpResult, IValueHttpResult, IValueHttpResult<IAsyncEnumerable<T>>
{
    readonly IEnumerable<T> _values = null!;
    readonly IAsyncEnumerable<T> _asyncValues = null!;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    internal Sequence(IEnumerable<T> values) => _values = values;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="values"></param>
    internal Sequence(IAsyncEnumerable<T> values) => _asyncValues = values;

    protected abstract ReadOnlyMemory<byte> Begin { get; }

    protected abstract ReadOnlyMemory<byte> End { get; }

    #region Value    
    object? IValueHttpResult.Value => Value;

    IAsyncEnumerable<T>? IValueHttpResult<IAsyncEnumerable<T>>.Value => Value;

    /// <summary>
    /// Gets the object result.
    /// </summary>
    public IAsyncEnumerable<T> Value => _get_value ??= GetValue();

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public abstract int StatusCode { get; }

    public abstract string ContentType { get; }

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
        var logger = httpContext.RequestServices.GetService<ILogger<JsonSequence<T>>>() ?? NullLogger<JsonSequence<T>>.Instance;
        
        httpContext.Response.StatusCode = StatusCode;
        if (string.IsNullOrEmpty(httpContext.Response.ContentType))
            httpContext.Response.ContentType = ContentType;

        var serializerOptions = (httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value ?? new JsonOptions()).SerializerOptions;
#if !NET8_0_OR_GREATER
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
#endif
        var jsonTypeInfo = serializerOptions.GetTypeInfo<T>();
        if (_asyncValues is not null)
            return InternalFormatWriter
                .WriteAsyncEnumerableAsync(
                    values: _asyncValues,
                    httpContext: httpContext,
                    SerializerOptions: serializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: Begin,
                    End: End,
                    SelectedEncoding: Encoding.UTF8,
                    logger: logger,
                    cancellationToken: httpContext.RequestAborted
                );
        if (_values is not null)
            return InternalFormatWriter
                .WriteEnumerableAsync(
                    values: _values,
                    httpContext: httpContext,
                    SerializerOptions: serializerOptions,
                    JsonTypeInfo: jsonTypeInfo,
                    Begin: Begin,
                    End: End,
                    SelectedEncoding: Encoding.UTF8,
                    logger: logger,
                    cancellationToken: httpContext.RequestAborted

                );
        return Task.CompletedTask;
    }
}
