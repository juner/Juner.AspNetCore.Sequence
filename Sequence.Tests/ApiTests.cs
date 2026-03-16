using Juner.AspNetCore.Sequence.Http;
using Juner.AspNetCore.Sequence.Http.HttpResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
#if NET8_0_OR_GREATER
using Microsoft.Extensions.Hosting;
#endif
namespace Juner.AspNetCore.Sequence;

[TestClass]
public class ApiTests
{
    public required TestContext TestContext { get; init; }
    CancellationToken CancellationToken => TestContext.CancellationTokenSource.Token;
    [TestMethod]
    public async Task NDJSON_Minimal_Post_Test()
    {
        using var server = await CreateServer();
        using var client = server.CreateClient();

        var body =
            "{\"name\":\"alice\",\"age\":30}\n" +
            "{\"name\":\"bob\",\"age\":25}\n";

        var content = new StringContent(body, Encoding.UTF8, "application/x-ndjson");

        var response = await client.PostAsync("/minimal/person/ndjson", content, CancellationToken);

        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Contains(body, text);
    }

    [TestMethod]
    public async Task NDJSON_Minimal_Post_Error_Test()
    {
        using var server = await CreateServer();
        using var client = server.CreateClient();

        var body =
            "{\"name\":\"alice\",\"age\":30}," +
            "{\"name\":\"bob\",\"age\":25}";

        var content = new StringContent(body, Encoding.UTF8, "application/json");

        await Assert.ThrowsAsync<Exception>(async () => await client.PostAsync("/minimal/person/ndjson", content, CancellationToken));
    }
    [TestMethod]
    public async Task JsonSequence_Minimal_Post_Test()
    {
        using var server = await CreateServer();
        using var client = server.CreateClient();

        var body =
            "\u001e{\"name\":\"alice\",\"age\":30}\n" +
            "\u001e{\"name\":\"bob\",\"age\":25}\n";

        var content = new StringContent(body, Encoding.UTF8, "application/json-seq");

        var response = await client.PostAsync("/minimal/person/json-seq", content, CancellationToken);

        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Contains(body, text);
    }

    [TestMethod]
    public async Task JsonLine_Minimal_Post_Test()
    {
        using var server = await CreateServer();
        using var client = server.CreateClient();

        var body =
            "{\"name\":\"alice\",\"age\":30}\n" +
            "{\"name\":\"bob\",\"age\":25}\n";

        var content = new StringContent(body, Encoding.UTF8, "application/jsonl");

        var response = await client.PostAsync("/minimal/person/jsonline", content, CancellationToken);

        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(CancellationToken);

        Assert.Contains(body, text);
    }

#if NET8_0_OR_GREATER
    async Task<TestServer> CreateServer()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(host =>
            {
                host.UseTestServer();

                host.ConfigureServices(ConfigureServices)
                .Configure(Configure);
            }).StartAsync(CancellationToken);

        return host.GetTestServer();
    }
#else
    static async Task<TestServer> CreateServer()
    {
        var host = new WebHostBuilder()
            .ConfigureServices(ConfigureServices)
            .Configure(Configure);
        return new TestServer(host);
    }
#endif
    static void ConfigureServices(IServiceCollection services)
     => services
        .AddControllers()
        .AddSequenceFormatter();
    static void Configure(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();

            var person = endpoints.MapGroup("/minimal/person/");
            person.MapPost("json-seq", (Sequence<Person> sequence) => SequenceResults.JsonSequence(sequence));
            person.MapPost("ndjson", (Sequence<Person> sequence) => SequenceResults.NdJson(sequence));
            person.MapPost("jsonline", (Sequence<Person> sequence) => SequenceResults.JsonLine(sequence));
            person.MapPost("json", (Sequence<Person> sequence) => sequence);
        });
    }
}
[ApiController]
[Route("/controller/")]
public class PersonController : ControllerBase
{
    [HttpPost("person/json-seq"), Consumes("application/json-seq")]
    public ActionResult<IAsyncEnumerable<Person>> JsonSequence(IAsyncEnumerable<Person> person) => Ok(person);

    [HttpPost("person/ndjson"), Consumes("application/x-ndjson")]
    public ActionResult<IAsyncEnumerable<Person>> NdJson(IAsyncEnumerable<Person> person) => Ok(person);
    [HttpPost("person/jsonline"), Consumes("application/jsonl")]
    public ActionResult<IAsyncEnumerable<Person>> JsonLine(IAsyncEnumerable<Person> person) => Ok(person);
    [HttpPost("person-result/json-seq"), Consumes("application/json-seq")]
    public JsonSequenceResult<Person> ResJsonSequence(IAsyncEnumerable<Person> person) => SequenceResults.JsonSequence(person);

    [HttpPost("person-result/ndjson"), Consumes("application/x-ndjson")]
    public NdJsonResult<Person> ResNdJson(IAsyncEnumerable<Person> person) => SequenceResults.NdJson(person);
    [HttpPost("person-result/jsonline"), Consumes("application/jsonl")]
    public JsonLineResult<Person> ResJsonLine(IAsyncEnumerable<Person> person) => SequenceResults.JsonLine(person);


}