using Juner.AspNetCore.Sequence.Mvc.Formatters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Threading.Channels;

namespace Juner.AspNetCore.Sequence;

[TestClass]
public class SequenceInputFormatterTests
{
    public required TestContext TestContext { get; set; }
    CancellationToken CancellationToken =>
#if NET8_0_OR_GREATER
        TestContext.CancellationToken;
#else
        TestContext.CancellationTokenSource.Token;
#endif

    record Person(string Name, int Age);

    [TestMethod]
    public async Task NDJSON_Read_List()
    {
        var body =
            "{\"Name\":\"Alice\",\"Age\":30}\n" +
            "{\"Name\":\"Bob\",\"Age\":25}\n";

        var formatter = new SequenceInputFormatter();

        var context = CreateContext<List<Person>>(
            "application/x-ndjson",
            body);

        var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        Assert.IsFalse(result.HasError);

        var list = result.Model as List<Person>;

        Assert.IsNotNull(list);
        Assert.HasCount(2, list);
        Assert.AreEqual("Alice", list[0].Name);
        Assert.AreEqual("Bob", list[1].Name);
    }

    [TestMethod]
    public async Task JsonSeq_Read_Array()
    {
        var body =
            "\u001e{\"Name\":\"Alice\",\"Age\":30}\n" +
            "\u001e{\"Name\":\"Bob\",\"Age\":25}\n";

        var formatter = new SequenceInputFormatter();

        var context = CreateContext<Person[]>(
            "application/json-seq",
            body);

        var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        var array = result.Model as Person[];

        Assert.IsNotNull(array);
        Assert.HasCount(2, array);
    }

    [TestMethod]
    public void CanRead_NDJSON()
    {
        var formatter = new SequenceInputFormatter();

        var context = CreateContext<List<Person>>(
            "application/x-ndjson",
            "");

        var canRead = formatter.CanRead(context);

        Assert.IsTrue(canRead);
    }

    [TestMethod]
    public async Task NDJSON_ChannelReader()
    {
        var body =
            "{\"Name\":\"Alice\",\"Age\":30}\n" +
            "{\"Name\":\"Bob\",\"Age\":25}\n";

        var formatter = new SequenceInputFormatter();

        var context = CreateContext<ChannelReader<Person>>(
            "application/x-ndjson",
            body);

        var result = await formatter.ReadRequestBodyAsync(context, Encoding.UTF8);

        var reader = result.Model as ChannelReader<Person>;

        Assert.IsNotNull(reader);

        var list = new List<Person>();

        await foreach (var p in reader.ReadAllAsync(CancellationToken))
            list.Add(p);

        Assert.HasCount(2, list);
    }

    static InputFormatterContext CreateContext<T>(
        string contentType,
        string body)
    {
        var httpContext = new DefaultHttpContext();

        var bytes = Encoding.UTF8.GetBytes(body);

        httpContext.Request.ContentType = contentType;
        httpContext.Request.Body = new MemoryStream(bytes);
        var services = new ServiceCollection();
        services.AddMvc();
        var provider = services
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();

        httpContext.RequestServices = provider;

        var metadataProvider = new EmptyModelMetadataProvider();

        var modelState = new ModelStateDictionary();

        return new InputFormatterContext(
            httpContext,
            "model",
            modelState,
            metadataProvider.GetMetadataForType(typeof(T)),
            (stream, encoding) => new StreamReader(stream, encoding));
    }
}
