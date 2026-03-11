using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Juner.AspNetCore.Sequence.Formatters;

public class JsonSequenceInputFormatter : TextInputFormatter
{
    public JsonSerializerOptions SerializerOptions { get; }

    const string ContentType =
#if NET8_0_OR_GREATER
        System.Net.Mime.MediaTypeNames.Application.JsonSequence;
#else
        "application/json-seq";
#endif

    public JsonSequenceInputFormatter(JsonSerializerOptions options)
    {
        SerializerOptions = options;

        SupportedMediaTypes.Add(ContentType);
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanReadType(Type type)
        => TryGetElementType(type, out _);

    public override bool CanRead(InputFormatterContext context)
    {
        if (!base.CanRead(context))
            return false;

        if (!TryGetElementType(context.ModelType, out _))
            return false;

        return true;
    }

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context,
        Encoding encoding)
    {
        var request = context.HttpContext.Request;

        if (!TryGetElementType(context.ModelType, out var elementType))
            return await InputFormatterResult.FailureAsync();

        var method = GetType()
            .GetMethod(nameof(ReadAsyncEnumerable), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(elementType);

        var result = method.Invoke(this, new object[]
        {
            request,
            context.HttpContext.RequestAborted
        });

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

    static bool TryGetElementType(Type type, [NotNullWhen(true)] out Type? elementType)
    {
        elementType = null;

        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();

            if (def == typeof(IAsyncEnumerable<>)
             || def == typeof(IEnumerable<>)
             || def == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType();
            return elementType != null;
        }

        return false;
    }
}