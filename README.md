# Juner.AspNetCore.Sequence

Streaming JSON sequence support for ASP.NET Core.

`Juner.AspNetCore.Sequence` is an ASP.NET Core formatter that provides
**streaming JSON support** for:

* NDJSON (`application/x-ndjson`)
* JSON Lines (`application/jsonl`)
* JSON Sequence (`application/json-seq`)
* JSON Array (`application/json`)

It enables **incremental serialization and deserialization** using
`IAsyncEnumerable<T>`, `IEnumerable<T>`, `ChannelReader<T>`, arrays, or lists.

---

## Why this library? (The Gap in ASP.NET Core)

While ASP.NET Core natively supports `IAsyncEnumerable<T>` for `application/json`,
it is limited to **JSON arrays (`[...]`)**.

For streaming-friendly formats like:

* NDJSON
* JSON Lines
* JSON Sequence

you typically need to:

* implement custom `InputFormatter` / `OutputFormatter`
* manually parse request bodies
* handle Minimal API binding yourself

**This library fills that gap** by enabling consistent streaming handling across formats.

---

## Quick Example (Minimal API)

```csharp
app.MapPost("/process", async (Sequence<Person> sequence) =>
{
    await foreach (var person in sequence)
    {
        Console.WriteLine($"Received: {person.Name}");
    }

    return Results.Ok();
});
```

Request (NDJSON):

```
{"name":"alice"}
{"name":"bob"}
```

---

## Installation

```bash
dotnet add package Juner.AspNetCore.Sequence
```

---

## Setup

```csharp
builder.Services.AddControllers()
    .AddSequenceFormatter();
```

---

## Features

* Supports multiple streaming formats

  * NDJSON
  * JSON Lines
  * JSON Sequence
  * JSON Array
* Supports multiple sequence sources

  * `IAsyncEnumerable<T>`
  * `IEnumerable<T>`
  * `T[]`
  * `List<T>`
  * `ChannelReader<T>`
* Minimal API ready (`Sequence<T>` binding)
* Results extensions (`Results.*`, `TypedResults.*`)
* Incremental JSON parsing
* Built on `System.Text.Json` and `PipeReader`
* Low memory usage (streaming, non-buffered)

---

## Usage

### Minimal API

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

### MVC Controller

```csharp
[HttpPost]
[Consumes("application/x-ndjson")]
public async Task<IActionResult> Post([FromBody] IAsyncEnumerable<Person> data)
{
    await foreach (var item in data)
    {
        Console.WriteLine(item.Name);
    }

    return Ok();
}
```

---

## Results APIs

This library provides multiple ways to return streaming responses.

### SequenceResults (baseline API)

```csharp
SequenceResults.NdJson(sequence)
```

Works in all environments without relying on extension methods.

---

### Results / TypedResults extensions

```csharp
Results.NdJson(sequence)
TypedResults.NdJson(sequence)
```

Provides a more natural Minimal API experience.

---

### Which should I use?

* Use `SequenceResults` if:

  * you need maximum compatibility
  * you prefer explicit usage

* Use `Results` / `TypedResults` if:

  * you are using modern ASP.NET Core
  * you prefer idiomatic Minimal API style

All APIs produce the same streaming behavior.

---

## Supported Formats

| Format        | RFC      | Content-Type         | Notes                            |
| ------------- | -------- | -------------------- | -------------------------------- |
| JSON Sequence | RFC 7464 | application/json-seq | record separator based           |
| NDJSON        | informal | application/x-ndjson | newline delimited                |
| JSON Lines    | informal | application/jsonl    | similar to NDJSON                |
| JSON Array    | RFC 8259 | application/json     | buffered or streaming-compatible |

---

## Supported Input Types

* `IAsyncEnumerable<T>`
* `IEnumerable<T>`
* `T[]`
* `List<T>`
* `ChannelReader<T>`
* `Sequence<T>`

---

## Supported Output Types

* `IAsyncEnumerable<T>`
* `IEnumerable<T>`
* `T[]`
* `List<T>`
* `ChannelReader<T>`

---

## Why not just use `[FromBody]`?

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

This expects a JSON array:

```json
[
 { "id": 1 },
 { "id": 2 }
]
```

It does **not** support streaming formats like NDJSON.

---

### With `Sequence<T>`

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

Request:

```
{"id":1}
{"id":2}
{"id":3}
```

---

## Comparison

| Feature             | Standard ASP.NET Core | With This Library |
| ------------------- | --------------------- | ----------------- |
| JSON Array          | ✅                     | ✅                 |
| NDJSON / JSONL      | ❌                     | ✅                 |
| JSON Sequence       | ❌                     | ✅                 |
| Minimal API binding | ⚠️ JSON only          | ✅                 |
| Streaming input     | ❌                     | ✅                 |
| Streaming output    | ⚠️ limited            | ✅                 |

---

## OpenAPI support

OpenAPI (Swagger) does not fully support streaming formats such as:

* NDJSON
* JSON Lines
* JSON Sequence

To avoid misleading schemas, response type information may be omitted.

Please refer to the examples for actual usage.

Support may improve with future OpenAPI versions (e.g. 3.2).

---

## Internals

The formatter integrates with ASP.NET Core's formatter pipeline.

Supported formats can be bound to multiple types, including:

* `IAsyncEnumerable<T>`
* `ChannelReader<T>`
* `Sequence<T>`

`Sequence<T>` acts as an optional abstraction for
format-agnostic streaming processing.

The implementation is based on:

* `System.Text.Json`
* `PipeReader`

Serialization and deserialization are performed incrementally.

---

## Target Framework

* .NET 7
* .NET 8
* .NET 9
* .NET 10
* ASP.NET Core

---

## License

[MIT](./LICENSE)
