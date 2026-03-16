using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence.Http.HttpResults;

[TestClass]
public class SequenceResultTests
{
    public required TestContext TestContext { get; set; }
    CancellationToken CancellationToken =>
#if NET8_0_OR_GREATER
        TestContext.CancellationToken;
#else
        TestContext.CancellationTokenSource.Token;
#endif

    static HttpContext CreateHttpContext(string? accept = null)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        ctx.Response.Body = new MemoryStream();

        if (accept != null)
            ctx.Request.Headers.Accept = accept;

        return ctx;
    }

    static async Task<string> GetResponseBody(HttpContext ctx, CancellationToken cancellationToken)
    {
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    [TestMethod]
    public async Task ExecuteAsync_NDJSON_FromEnumerable()
    {
        var values = new[]
        {
            new Person("alice",30),
            new Person("bob",25)
        };

        var result = new SequenceResult<Person>(values, "application/x-ndjson");

        var ctx = CreateHttpContext("application/x-ndjson");

        await result.ExecuteAsync(ctx);

        var body = await GetResponseBody(ctx, CancellationToken);

        Assert.IsTrue(body.Contains("alice"));
        Assert.IsTrue(body.Contains("bob"));
        Assert.AreEqual("application/x-ndjson", ctx.Response.ContentType);
        Assert.AreEqual(200, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task ExecuteAsync_NDJSON_FromAsyncEnumerable()
    {
        static async IAsyncEnumerable<Person> GetValues()
        {
            yield return new Person("alice", 30);
            yield return new Person("bob", 25);
            await Task.CompletedTask;
        }

        var result = new SequenceResult<Person>(GetValues(), "application/x-ndjson");

        var ctx = CreateHttpContext("application/x-ndjson");

        await result.ExecuteAsync(ctx);

        var body = await GetResponseBody(ctx, CancellationToken);

        Assert.IsTrue(body.Contains("alice"));
        Assert.IsTrue(body.Contains("bob"));
    }

    [TestMethod]
    public async Task ExecuteAsync_NDJSON_FromChannelReader()
    {
        var channel = Channel.CreateUnbounded<Person>();

        _ = Task.Run(async () =>
        {
            await channel.Writer.WriteAsync(new Person("alice", 30));
            await channel.Writer.WriteAsync(new Person("bob", 25));
            channel.Writer.Complete();
        });

        var result = new SequenceResult<Person>(channel.Reader, "application/x-ndjson");

        var ctx = CreateHttpContext("application/x-ndjson");

        await result.ExecuteAsync(ctx);

        var body = await GetResponseBody(ctx, CancellationToken);

        Assert.IsTrue(body.Contains("alice"));
        Assert.IsTrue(body.Contains("bob"));
    }

    [TestMethod]
    public async Task ExecuteAsync_Json_Array_Mode()
    {
        var values = new[]
        {
            new Person("alice",30),
            new Person("bob",25)
        };

        var result = new SequenceResult<Person>(values, "application/json");

        var ctx = CreateHttpContext("application/json");

        await result.ExecuteAsync(ctx);

        var body = await GetResponseBody(ctx, CancellationToken);

        Assert.IsTrue(body.StartsWith("["));
        Assert.IsTrue(body.Contains("alice"));
        Assert.IsTrue(body.Contains("bob"));
    }

    [TestMethod]
    public void Constructor_InvalidContentType_Throws()
        => Assert.Throws<ArgumentException>(() =>
            new SequenceResult<Person>(
            [new Person("alice", 30)],
            "text/plain"
        ));

    [TestMethod]
    public async Task ExecuteAsync_InvalidAccept_Throws()
    {
        var values = new[]
        {
            new Person("alice",30)
        };

        var result = new SequenceResult<Person>(values, "application/x-ndjson");

        var ctx = CreateHttpContext("text/plain");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            result.ExecuteAsync(ctx));
    }
}