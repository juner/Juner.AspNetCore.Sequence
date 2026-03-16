using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
#if NET9_0_OR_GREATER
using System.Net.Mime;
#endif

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

[TestClass]
public class JsonSequenceResultTests
{
    public required TestContext TestContext { get; set; }
    CancellationToken CancellationToken =>
#if NET8_0_OR_GREATER
        TestContext.CancellationToken;
#else
        TestContext.CancellationTokenSource.Token;
#endif

    [TestMethod]
    public async Task Enumerable_StatusCodeAndValueTest()
    {
        var result = new JsonSequenceResult<string>(["test1", "test2"]);
        Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual("application/json-seq", result.ContentType);
        string[] expected = ["test1", "test2"];
        var actual = await ToArrayAsync(result.ToAsyncEnumerable(CancellationToken), CancellationToken);
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task AsyncEnumerable_StatusCodeAndValueTest()
    {
        var result = new JsonSequenceResult<string>(GetValue());
        static async IAsyncEnumerable<string> GetValue()
        {
            await Task.Yield();
            yield return "test1";
            yield return "test2";
        }
        var statusCode = result.StatusCode;
        Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        Assert.AreEqual(statusCode, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual("application/json-seq", result.ContentType);
        string[] expected = ["test1", "test2"];
        var actual = await ToArrayAsync(result.ToAsyncEnumerable(CancellationToken), CancellationToken);
        CollectionAssert.AreEqual(expected, actual);
    }
    public async Task ChannelReader_StatusCodeAndValueTest()
    {
        var channel = Channel.CreateBounded<string>(1);
        var result = new JsonSequenceResult<string>(channel);
        var statusCode = result.StatusCode;
        Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
        Assert.AreEqual(statusCode, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual("application/json-seq", result.ContentType);
        string[] expected = ["test1", "test2"];
        var asyncEnumerable = result.ToAsyncEnumerable(CancellationToken);

        _ = channel.Writer.WriteAsync("test1", CancellationToken);
        _ = channel.Writer.WriteAsync("test2", CancellationToken);
        channel.Writer.Complete();
        var actual = await ToArrayAsync(asyncEnumerable, CancellationToken);
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task Empty_ValueTest()
    {
        IEnumerable<string> v = null!;
        var result = new JsonSequenceResult<string>(v);
        var array = await ToArrayAsync(result.ToAsyncEnumerable(CancellationToken), CancellationToken);
        CollectionAssert.AreEqual(Array.Empty<string>(), array);
    }

    [TestMethod]
    public async Task Enumerable_ExecuteAsyncTest()
    {
        var result = new JsonSequenceResult<string>(["test1", "test2"]);

        await using var stream = new MemoryStream();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = CreateServices(),
            Response = {
                Body = stream,
            }
        };
        await result.ExecuteAsync(httpContext);

        Assert.AreEqual("\u001e\"test1\"\n\u001e\"test2\"\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [TestMethod]
    public async Task AsyncEnumerable_ExecuteAsyncTest()
    {
        var result = new JsonSequenceResult<string>(GetValue());
        static async IAsyncEnumerable<string> GetValue()
        {
            await Task.Yield();
            yield return "test1";
            yield return "test2";
        }
        await using var stream = new MemoryStream();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = CreateServices(),
            Response = {
                Body = stream,
            }
        };
        await result.ExecuteAsync(httpContext);

        Assert.AreEqual("\u001e\"test1\"\n\u001e\"test2\"\n", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [TestMethod]
    public async Task Empty_ExecuteAsyncTest()
    {
        IEnumerable<string> v = null!;
        var result = new JsonSequenceResult<string>(v);

        await using var stream = new MemoryStream();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = CreateServices(),
            Response = {
                Body = stream,
            }
        };
        await result.ExecuteAsync(httpContext);
        Assert.AreEqual(string.Empty, Encoding.UTF8.GetString(stream.ToArray()));
    }

    record Todo(string Task);

    [TestMethod]
    public void PopulateMetadataTest()
    {
        static JsonSequenceResult<Todo> MyApi() => throw new NotImplementedException();
        var metadata = new List<object>();
        var builder = new RouteEndpointBuilder(requestDelegate: null, RoutePatternFactory.Parse("/"), order: 0);

        // Act
        PopulateMetadata<JsonSequenceResult<Todo>>(((Delegate)MyApi).GetMethodInfo(), builder);

        // Assert
        var producesResponseTypeMetadata = builder.Metadata.OfType<IProducesResponseTypeMetadata>().Last();
        Assert.AreEqual(StatusCodes.Status200OK, producesResponseTypeMetadata.StatusCode);
        Assert.IsNull(producesResponseTypeMetadata.Type);
        var producesSequenceResponseTypeMetadata = builder.Metadata.OfType<IProducesSequenceResponseTypeMetadata>().Last();
        Assert.AreEqual(typeof(Todo), producesSequenceResponseTypeMetadata?.ItemType);
        var jsonSequence =
#if NET9_0_OR_GREATER
                MediaTypeNames.Application.JsonSequence;
#else
            "application/json-seq";
#endif
        Assert.AreEqual(jsonSequence, producesResponseTypeMetadata.ContentTypes?.FirstOrDefault());

        Console.WriteLine(string.Join(", ", builder.Metadata.Select(v => v.GetType().FullName)));
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static void PopulateMetadata<TResult>(MethodInfo method, EndpointBuilder builder)
        where TResult : IEndpointMetadataProvider => TResult.PopulateMetadata(method, builder);

    // Local helper to avoid relying on external ToArrayAsync extension methods.
    private static async Task<T[]> ToArrayAsync<T>(IAsyncEnumerable<T>? source, CancellationToken cancellationToken)
    {
        if (source == null)
        {
            return Array.Empty<T>();
        }

        var list = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list.ToArray();
    }
}