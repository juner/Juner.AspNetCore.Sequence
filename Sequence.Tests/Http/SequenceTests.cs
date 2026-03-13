using Juner.AspNetCore.Sequence.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Juner.AspNetCore.Http;

[TestClass]
public class SequenceTests
{
    record Person(string Name, int Age);

    static DefaultHttpContext CreateContext(string contentType, string body)
    {
        var context = new DefaultHttpContext();

        var bytes = Encoding.UTF8.GetBytes(body);

        context.Request.ContentType = contentType;
        context.Request.Body = new MemoryStream(bytes);
        context.RequestServices = GetServices();

        return context;
    }

    static async Task<List<T>> ReadAll<T>(Sequence<T> seq)
    {
        var list = new List<T>();

        await foreach (var item in seq)
            list.Add(item);

        return list;
    }

    [TestMethod]
    public async Task JsonSeq_Parse()
    {
        var body =
            "\u001e{\"Name\":\"Alice\",\"Age\":30}\n" +
            "\u001e{\"Name\":\"Bob\",\"Age\":25}\n";

        var ctx = CreateContext("application/json-seq", body);

        var parameter = new MockParameterInfo(typeof(Sequence<Person>), "persons");
        var seq = await CallBindAsync<Person>(ctx, parameter);

        Assert.IsNotNull(seq);

        var list = await ReadAll(seq!);

        Assert.HasCount(2, list);
        Assert.AreEqual("Alice", list[0].Name);
        Assert.AreEqual("Bob", list[1].Name);
    }

    [TestMethod]
    public async Task JsonSeq_LastFrameWithoutLF()
    {
        var body =
            "\u001e{\"Name\":\"Alice\",\"Age\":30}\n" +
            "\u001e{\"Name\":\"Bob\",\"Age\":25}";

        var ctx = CreateContext("application/json-seq", body);

        var parameter = new MockParameterInfo(typeof(Sequence<Person>), "persons");
        var seq = await CallBindAsync<Person>(ctx, parameter);

        var list = await ReadAll(seq!);

        Assert.HasCount(2, list);
        Assert.AreEqual("Bob", list[1].Name);
    }

    [TestMethod]
    public async Task NDJSON_Parse()
    {
        var body =
            "{\"Name\":\"Alice\",\"Age\":30}\n" +
            "{\"Name\":\"Bob\",\"Age\":25}\n";

        var ctx = CreateContext("application/x-ndjson", body);

        var parameter = new MockParameterInfo(typeof(Sequence<Person>), "persons");
        var seq = await CallBindAsync<Person>(ctx, parameter);

        var list = await ReadAll(seq!);

        Assert.HasCount(2, list);
        Assert.AreEqual("Alice", list[0].Name);
        Assert.AreEqual("Bob", list[1].Name);
    }

    [TestMethod]
    public async Task JSON_Array_Stream()
    {
        var body =
        """
        [
          {"Name":"Alice","Age":30},
          {"Name":"Bob","Age":25}
        ]
        """;

        var ctx = CreateContext("application/json", body);

        var parameter = new MockParameterInfo(typeof(Sequence<Person>), "persons");
        var seq = await CallBindAsync<Person>(ctx, parameter);

        var list = await ReadAll(seq!);

        Assert.HasCount(2, list);
        Assert.AreEqual("Alice", list[0].Name);
        Assert.AreEqual("Bob", list[1].Name);
    }

    [TestMethod]
    public async Task NDJSON_MultipleLines()
    {
        var sb = new StringBuilder();

        for (var i = 0; i < 100; i++)
        {
            sb.Append(JsonSerializer.Serialize(new Person($"User{i}", i)));
            sb.Append('\n');
        }

        var ctx = CreateContext("application/x-ndjson", sb.ToString());

        var parameter = new MockParameterInfo(typeof(Sequence<Person>), "persons");
        var seq = await CallBindAsync<Person>(ctx, parameter);

        var list = await ReadAll(seq!);

        Assert.HasCount(100, list);
        Assert.AreEqual("User0", list[0].Name);
        Assert.AreEqual("User99", list[99].Name);
    }

    static async Task<Sequence<T>?> CallBindAsync<T>(HttpContext ctx, ParameterInfo parameter)
    {
        var HttpContextExpr = ParameterBindingMethodCache.SharedExpressions.HttpContextExpr;
        var bindAsyncMethod = ParameterBindingMethodCache.Instance.FindBindAsyncMethod(parameter);
        var bindAsyncDelegate = Expression.Lambda<Func<HttpContext, ValueTask<object?>>>(bindAsyncMethod.Expression!, HttpContextExpr).Compile();
        var result = await bindAsyncDelegate.Invoke(ctx);
        return result as Sequence<T>;
    }
    private class MockParameterInfo : ParameterInfo
    {
        public MockParameterInfo(Type type, string name)
        {
            ClassImpl = type;
            NameImpl = name;
        }
    }
    static IServiceProvider GetServices()
    {
        var service = new ServiceCollection();
        return service.BuildServiceProvider();

    }
}
