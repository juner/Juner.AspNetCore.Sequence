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

Instead of buffering the entire response, objects can be **written and parsed incrementally**, making it suitable for:

- large datasets
- real-time streaming APIs
- pipeline-based processing

Instead of buffering the entire response, objects can be **serialized and written incrementally**, making it suitable for:

- large datasets
- real-time streaming APIs
- pipeline-based processing

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

| Format | RFC | Content-Type | |
|------|------|------|---|
| JSON Sequence | RFC 7464 | application/json-seq | |
| NDJSON | informal standard | application/x-ndjson | | 
| JSON Lines | informal standard | application/jsonl | |
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
var person = endpoints.MapGroup("/minimal/person/");

person.MapPost("json-seq",
    (Sequence<Person> sequence) => SequenceResults.JsonSequence(sequence));

person.MapPost("ndjson",
    (Sequence<Person> sequence) => SequenceResults.NdJson(sequence));

person.MapPost("jsonline",
    (Sequence<Person> sequence) => SequenceResults.JsonLine(sequence));

person.MapPost("json",
    (Sequence<Person> sequence) => SequenceResults.Sequence(sequence));
```

| Endpoint | Input | Output |
|--------|------|------|
| `/json-seq` | json / ndjson / jsonl / json-seq | json-seq |
| `/ndjson` | json / ndjson / jsonl / json-seq | ndjson |
| `/jsonline` | json / ndjson / jsonl / json-seq | jsonl |
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

    [HttpPost("person/jsonline")]
    [Consumes("application/jsonl")]
    [Produces("application/jsonl")]
    public ActionResult<IAsyncEnumerable<Person>>
        JsonLine([FromBody] IAsyncEnumerable<Person> person)
        => Ok(person);
}
```

---

# Example request

NDJSON request body:

```
{"name":"alice","age":30}
{"name":"bob","age":25}
```

JSON Sequence request body:

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

# Example

Request body (JSON array):

```json
[
 { "id": 1 },
 { "id": 2 },
 { "id": 3 }
]
```

Controller:

```csharp
[HttpPost]
public async Task<IActionResult> Post(IAsyncEnumerable<MyObject> items)
{
    await foreach (var item in items)
    {
        Console.WriteLine(item.Id);
    }

    return Ok();
}
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

Internally, all formats are converted to `Sequence<T>`,
which acts as a unified streaming abstraction.

The formatter uses:

- `System.Text.Json`
- `PipeReader`
- `IAsyncEnumerable<T>`
- `ChannelReader<T>`

Serialization and deserialization are performed incrementally to avoid full buffering.

---

# Target Framework

- .NET 7
- .NET 8
- .NET 9
- .NET 10
- ASP.NET Core

---

# License

[MIT](./LICENSE)