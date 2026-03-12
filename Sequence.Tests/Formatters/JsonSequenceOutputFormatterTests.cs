using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Juner.AspNetCore.Sequence.Formatters;

[TestClass]
public class JsonSequenceOutputFormatterTests
{
    public required TestContext Context { get; set; }

    static IEnumerable<object?[]> CanWriteResultTestData
    {
        get
        {
            yield return CanWriteResult(null, null, false);
            yield return CanWriteResult("application/json-seq", typeof(IAsyncEnumerable<int>), true);
            yield return CanWriteResult("application/json-seq", typeof(IEnumerable<int>), true);
            yield return CanWriteResult("application/json-seq", typeof(List<int>), true);
            yield return CanWriteResult("application/json", typeof(IAsyncEnumerable<int>), false);
            // 文字列は対応していない
            yield return CanWriteResult("application/json-seq", typeof(string), false);
            static object?[] CanWriteResult(string? accept, Type? objectType, bool expect)
              => [accept, objectType, expect];
        }
    }

    [TestMethod]
    [DynamicData(nameof(CanWriteResultTestData))]
    public void CanWriteResultTest(string? accept, Type? objectType, bool expect)
    {
        OutputFormatterCanWriteContext context;
        {
            var writerFactory = new TestHttpResponseStreamWriterFactory().CreateWriter;
            var httpContext = MakeHttpContext(accept);
            object? @object = null;
            context = new OutputFormatterWriteContext(httpContext, writerFactory, objectType, @object);
        }
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        var formatter = new JsonSequenceOutputFormatter();
        var actual = formatter.CanWriteResult(context);
        Assert.AreEqual(expect, actual);


    }
    static DefaultHttpContext MakeHttpContext(string? accept)
    {
        var httpContext = new DefaultHttpContext()
        {
            RequestServices = CreateServices(),
        };
        if (!string.IsNullOrEmpty(accept))
            httpContext.Request.Headers.Accept = accept;
        return httpContext;
    }


    static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>(params T[] args)
    {
        foreach (var a in args)
            yield return a;
    }
    static IEnumerable<object?[]> WriteResultTestData
    {
        get
        {
            {

                var accept = "application/json-seq";
                var acceptEncoding = "utf-8";
                var objectType = typeof(IAsyncEnumerable<int>);
                object? @object =
                    AsyncEnumerableEmpty<int>();
                var expectedContentType = "application/json-seq; charset=utf-8";
                var expectedBody = "";
                yield return WriteResultTest(accept, acceptEncoding, objectType, @object, expectedContentType, expectedBody);
            }
            {

                var accept = "application/json-seq";
                var acceptEncoding = "utf-8";
                var objectType = typeof(IAsyncEnumerable<string>);
                object? @object =
                    AsyncEnumerableEmpty("1", "2", "3");
                var expectedContentType = "application/json-seq; charset=utf-8";
                var expectedBody = "\u001e\"1\"\n\u001e\"2\"\n\u001e\"3\"\n";
                yield return WriteResultTest(accept, acceptEncoding, objectType, @object, expectedContentType, expectedBody);
            }
            static object?[] WriteResultTest(string? accept, string? acceptEncoding, Type objectType, object? @object, string expectedContentType, string expectedBody)
              => [accept, acceptEncoding, objectType, @object, expectedContentType, expectedBody];
        }
    }
    [TestMethod]
    [DynamicData(nameof(WriteResultTestData))]
    public async Task WriteResultTest(string accept, string acceptEncoding, Type objectType, object? @object, string expectedContentType, string expectedBody)
    {
        OutputFormatterWriteContext context;
        {
            var writerFactory = new TestHttpResponseStreamWriterFactory().CreateWriter;
            var httpContext = MakeHttpContext(accept, acceptEncoding);
            context = new OutputFormatterWriteContext(httpContext, writerFactory, objectType, @object);
        }
        var formatter = new JsonSequenceOutputFormatter();
        using var stream = new MemoryStream();
        context.HttpContext.Response.Body = stream;
        await formatter.WriteAsync(context);
        var actualContentType = context.HttpContext.Response.ContentType;
        Assert.AreEqual(expectedContentType, actualContentType);
        var actualBody = Encoding.GetEncoding(acceptEncoding).GetString(stream.ToArray());
        Assert.AreEqual(expectedBody, actualBody);
    }
    static DefaultHttpContext MakeHttpContext(string? accept, string? acceptEncoding)
    {
        var httpContext = new DefaultHttpContext()
        {
            RequestServices = CreateServices(),
        };
        if (!string.IsNullOrEmpty(accept))
            httpContext.Request.Headers.Accept = accept;
        if (!string.IsNullOrEmpty(acceptEncoding))
            httpContext.Request.Headers.AcceptEncoding = acceptEncoding;
        return httpContext;
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}