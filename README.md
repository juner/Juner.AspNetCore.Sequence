# Juner.AspNetCore.Sequence

Streaming JSON sequence support for ASP.NET Core.

`Juner.AspNetCore.Sequence` is an ASP.NET Core formatter that provides
**streaming JSON support** for:

- NDJSON
- JSON Lines
- JSON Sequence
- JSON arrays

It enables **incremental serialization and deserialization**
using `IAsyncEnumerable<T>`, `IEnumerable<T>`, `ChannelReader<T>`, arrays, or lists.

Objects are processed **incrementally instead of buffering the entire payload**, making it suitable for:

- large datasets
- real-time streaming APIs
- pipeline-based processing

---

## Quick Example (NDJSON streaming)

```csharp
app.MapPost("/ndjson",
    async (Sequence<Person> sequence) =>
    {
        await foreach (var item in sequence)
        {
            Console.WriteLine(item.Name);
        }

        return Results.Ok();
    });
```

Request:

```
{"name":"alice"}
{"name":"bob"}
```

---

# Features

- Streaming JSON output
- Supports streaming formats
  - NDJSON (`application/x-ndjson`)
  - JSON Lines (`application/jsonl`)
  - JSON Sequence (`application/json-seq`)
  - JSON Array (`application/json`)
- Supports multiple sequence sources
  - `IAsyncEnumerable<T>`
  - `IEnumerable<T>`
  - `T[]`
  - `List<T>`
  - `ChannelReader<T>`
- Incremental JSON parsing
- Works with `System.Text.Json`
- Designed for ASP.NET Core minimal APIs and MVC
- Zero-buffer or low-buffer streaming

---

# Supported Formats

This library supports the following streaming JSON formats:

| Format | RFC | Content-Type | Notes |
|------|------|------|------|
| JSON Sequence | RFC 7464 | application/json-seq | record separator based |
| NDJSON | informal standard | application/x-ndjson | newline delimited |
| JSON Lines | informal standard | application/jsonl | similar to NDJSON |
| JSON Array | RFC 8259 | application/json | buffered or streaming-compatible |

---

# Installation

```bash
dotnet add package Juner.AspNetCore.Sequence
```

---

# Usage

## Register formatter

Register the formatter in ASP.NET Core.

```csharp
builder.Services.AddControllers()
    .AddSequenceFormatter();
```

or manual registration:

```csharp
builder.Services.AddControllers(options =>
{
    options.InputFormatters.Insert(0, new SequenceInputFormatter());
    options.OutputFormatters.Insert(0, new NdJsonOutputFormatter());
    options.OutputFormatters.Insert(0, new JsonSequenceOutputFormatter());
    options.OutputFormatters.Insert(0, new JsonLineOutputFormatter());
});
```

---

## Minimal API

`Sequence<T>` can be used directly as a parameter in Minimal API.

```csharp
var person = app.MapGroup("/minimal/person/");

person.MapPost("json-seq",
    (Sequence<Person> sequence) => SequenceResults.JsonSequence(sequence));

person.MapPost("ndjson",
    (Sequence<Person> sequence) => SequenceResults.NdJson(sequence));

person.MapPost("jsonl",
    (Sequence<Person> sequence) => SequenceResults.JsonLine(sequence));

person.MapPost("json",
    (Sequence<Person> sequence) => SequenceResults.Sequence(sequence));
```

| Endpoint | Input | Output |
|--------|------|------|
| `/json-seq` | json / ndjson / jsonl / json-seq | json-seq |
| `/ndjson` | json / ndjson / jsonl / json-seq | ndjson |
| `/jsonl` | json / ndjson / jsonl / json-seq | jsonl |
| `/json` | json / ndjson / jsonl / json-seq | negotiation |

### Restricting accepted content type

You can restrict request content types using `Accepts`.

```csharp
person.MapPost("only-json-seq",
    (Sequence<Person> sequence) =>
        SequenceResults.Sequence(sequence, "application/json-seq"))
    .Accepts<Sequence<Person>>("application/json-seq");

person.MapPost("only-ndjson",
    (Sequence<Person> sequence) =>
        SequenceResults.Sequence(sequence, "application/x-ndjson"))
    .Accepts<Sequence<Person>>("application/x-ndjson");
```

---

# MVC Controller

MVC controllers can use `IAsyncEnumerable<T>` as the streaming body type.

```csharp
[ApiController]
[Route("/controller/")]
public class PersonController : ControllerBase
{
    [HttpPost("person/json-seq")]
    [Consumes("application/json-seq")]
    [Produces("application/json-seq")]
    public ActionResult<IAsyncEnumerable<Person>>
        JsonSequence([FromBody] IAsyncEnumerable<Person> person)
        => Ok(person);

    [HttpPost("person/ndjson")]
    [Consumes("application/x-ndjson")]
    [Produces("application/x-ndjson")]
    public ActionResult<IAsyncEnumerable<Person>>
        NdJson([FromBody] IAsyncEnumerable<Person> person)
        => Ok(person);

    [HttpPost("person/jsonl")]
    [Consumes("application/jsonl")]
    [Produces("application/jsonl")]
    public ActionResult<IAsyncEnumerable<Person>>
        JsonLine([FromBody] IAsyncEnumerable<Person> person)
        => Ok(person);
}
```

---

# Example request formats

## NDJSON

```
{"name":"alice","age":30}
{"name":"bob","age":25}
```

### JSON Sequence

```
\u001e{"name":"alice","age":30}
\u001e{"name":"bob","age":25}
```

---

# Supported Output Types

These types can be used as action results for streaming responses.

| Type | Description |
|---|---|
| `IAsyncEnumerable<T>` | Async streaming sequence |
| `IEnumerable<T>` | Synchronous sequence |
| `T[]` | Array |
| `List<T>` | List |
| `ChannelReader<T>` | Channel streaming |

---

# Supported Input Types

Incoming JSON streams (json / ndjson / jsonl / json-seq)
can be parsed into:

- `IAsyncEnumerable<T>`
- `IEnumerable<T>`
- `T[]`
- `List<T>`
- `ChannelReader<T>`

This allows **streaming request bodies** without loading the entire payload.

---

# Why not just use `[FromBody]`?

This does NOT work for streaming formats:

```csharp
app.MapPost("/json",
    async ([FromBody] IAsyncEnumerable<MyObject> items) =>
    {
        await foreach (var item in items)
        {
            Console.WriteLine(item.Id);
        }
    });
```

Because it expects a JSON array:

```json
[
 { "id": 1 },
 { "id": 2 }
]
```

---

With `Sequence<T>`, you can process streaming formats:

```csharp
app.MapPost("/ndjson",
    async (Sequence<MyObject> sequence) =>
    {
        await foreach (var item in sequence)
        {
            Console.WriteLine(item.Id);
        }
    });
```

Request (NDJSON):

```
{"id":1}
{"id":2}
{"id":3}
```

---

# Why Streaming?

Typical JSON APIs buffer the entire collection before sending.
This library enables **streaming serialization and deserialization**.

```
[ server ]
   ↓
build array
   ↓
serialize
   ↓
send response
```

Streaming APIs can start sending data immediately.

```
yield object
   ↓
serialize
   ↓
write to response
   ↓
repeat
```

Benefits:

- lower memory usage
- faster first-byte response
- real-time streaming APIs

---

# Internals

The formatter integrates with ASP.NET Core's formatter pipeline.

Supported input formats (application/json, application/json-seq,
application/x-ndjson, application/jsonl) can be bound to:

- `IAsyncEnumerable<T>`
- `IEnumerable<T>`
- `T[]`
- `List<T>`
- `ChannelReader<T>`
- `Sequence<T>`

`Sequence<T>` is an optional abstraction designed for
stream-oriented processing and format-agnostic handling.

The implementation is based on:

- `System.Text.Json`
- `PipeReader`
- `IAsyncEnumerable<T>`
- `ChannelReader<T>`

Serialization and deserialization are performed incrementally
to avoid full buffering.

## Results APIs

This library provides multiple ways to return streaming responses.
All APIs produce the same streaming behavior.

### SequenceResults (baseline API)

```csharp
SequenceResults.NdJson(sequence)
```

This API works in all supported environments and does not rely on
extension methods.

---

### Results / TypedResults extensions

```csharp
Results.NdJson(sequence)
TypedResults.NdJson(sequence)
```

These extension methods provide a more natural integration with
ASP.NET Core Minimal APIs.

---

### Which should I use?

- Use `SequenceResults` if:
  - you are using older language versions
  - you want explicit and dependency-free usage

- Use `Results` / `TypedResults` if:
  - you are using modern ASP.NET Core
  - you prefer a more idiomatic Minimal API style

---

## Minimal API limitations

ASP.NET Core Minimal APIs do not use the MVC InputFormatter pipeline.

As a result, non-standard streaming formats such as:

- application/x-ndjson
- application/jsonl
- application/json-seq

cannot be automatically bound to types like `IAsyncEnumerable<T>`.

To support these formats in Minimal APIs, this library provides
`Sequence<T>`, which acts as a custom binding entry point.

---

# Target Framework

- .NET 7
- .NET 8
- .NET 9
- .NET 10
- ASP.NET Core

---

## OpenAPI support

OpenAPI (Swagger) does not fully support streaming formats such as
NDJSON, JSON Lines, or JSON Sequence.

To avoid misleading schemas, response type information may be omitted.

Please refer to the examples for actual usage.

Support may improve with future OpenAPI versions (e.g. 3.2).

---

# License

[MIT](./LICENSE)