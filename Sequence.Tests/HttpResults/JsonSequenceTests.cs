using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Mime;
using System.Reflection;
using System.Text;

namespace Juner.AspNetCore.Sequence.HttpResults;

[TestClass]
public class JsonSequenceTests
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
		var result = new JsonSequence<string>(["test1", "test2"]);
		Assert.AreEqual(StatusCodes.Status200OK, result.StatusCode);
		Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)result).StatusCode);
		Assert.AreEqual("application/json-seq", result.ContentType);
		string[] expected = ["test1", "test2"];
		var value = result.Value;
		var actual = await ToArrayAsync(value, CancellationToken);
		CollectionAssert.AreEqual(expected, actual);
		Assert.AreEqual(value, ((IValueHttpResult<IAsyncEnumerable<string>>)result).Value);
		Assert.AreEqual(value, ((IValueHttpResult)result).Value);
	}

	[TestMethod]
	public async Task AsyncEnumerable_StatusCodeAndValueTest()
	{
		var result = new JsonSequence<string>(GetValue());
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
		var value = result.Value;
		var actual = await ToArrayAsync(value, CancellationToken);
		CollectionAssert.AreEqual(expected, actual);
		Assert.AreEqual(value, ((IValueHttpResult<IAsyncEnumerable<string>>)result).Value);
		Assert.AreEqual(value, ((IValueHttpResult)result).Value);
	}

	[TestMethod]
	public async Task Empty_ValueTest()
	{
		IEnumerable<string> v = null!;
		var result = new JsonSequence<string>(v);
		var array = await ToArrayAsync(result.Value, CancellationToken);
		CollectionAssert.AreEqual(Array.Empty<string>(), array);
	}

	[TestMethod]
	public async Task Enumerable_ExecuteAsyncTest()
	{
		var result = new JsonSequence<string>(["test1", "test2"]);

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
		var result = new JsonSequence<string>(GetValue());
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
		var result = new JsonSequence<string>(v);

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
		static JsonSequence<Todo> MyApi() => throw new NotImplementedException();
		var metadata = new List<object>();
		var builder = new RouteEndpointBuilder(requestDelegate: null, RoutePatternFactory.Parse("/"), order: 0);

		// Act
		PopulateMetadata<JsonSequence<Todo>>(((Delegate)MyApi).GetMethodInfo(), builder);

		// Assert
		var producesResponseTypeMetadata = builder.Metadata.OfType<ProducesResponseTypeMetadata>().Last();
		Assert.AreEqual(StatusCodes.Status200OK, producesResponseTypeMetadata.StatusCode);
		Assert.AreEqual(typeof(IAsyncEnumerable<Todo>), producesResponseTypeMetadata.Type);
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
		services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
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
