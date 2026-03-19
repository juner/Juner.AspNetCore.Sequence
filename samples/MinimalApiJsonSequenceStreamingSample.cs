#!/usr/local/share/dotnet/dotnet run
#:sdk Microsoft.NET.Sdk.Web
#:project ../Sequence/Juner.AspNetCore.Sequence.csproj
#:property UserSecretsId=DaiwaHouseUketsuke.Web       
#:property RootNamespace=Juner.AspNetCore.Sequence.Net10AspNetCoreMvcSample
#:property TargetFramework=net10.0
#:property TargetFrameworks=net10.0
#:package Microsoft.AspNetCore.OpenApi@10.0.5

using Juner.AspNetCore.Sequence.Http;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options
 => options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Minimal API JSON Sequence Streaming Sample";
        return Task.CompletedTask;
    }))
    .AddSequenceOpenApi();

builder.Services.AddLogging(
    static builder => {
        builder.AddSimpleConsole(options =>
            options.IncludeScopes = true
        );
        builder.AddFilter(level => true);    
    }
);

builder.Services.ConfigureHttpJsonOptions(options
 => options.SerializerOptions.TypeInfoResolver = JsonTypeInfoResolver.Combine(
        JsonSchemaContext.Default
    ));

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.MapGet("/", () => TypedResults.Content($$"""
<title>application/json-seq input / output sample</title>
<style>
    .request {
        color: green;
        background: white;
    }
    .response {
        color: blue;
        background: white;
    }
    .error {
        color: white;
        background: red;
    }
</style>
<script type="module">
import { InputJsonSequenceStream, OutputJsonSequenceStream } from 'https://cdn.jsdelivr.net/npm/json-seq-stream@1.0.10/+esm'

const supportsRequestStreams = (() => {
  let duplexAccessed = false;

  const hasContentType = new Request('', {
    body: new ReadableStream(),
    method: 'POST',
    get duplex() {
      duplexAccessed = true;
      return 'half';
    },
  }).headers.has('Content-Type');

  return duplexAccessed && !hasContentType;
})();

if (supportsRequestStreams) {
    additionNum.disabled = false;
    additionButton.disabled = false;
    resultButton.disabled = true;
    responseInfo(`supportsRequestStreams: true`);
} else {
    error(`supportsRequestStreams: false`);
}

countUpButton.addEventListener("click", onCountUpStart);
additionButton.addEventListener("click", onClickAddition);
resultButton.addEventListener("click", onClickAdditionResult)
let countUpAct = undefined;
let additionAct = undefined;
let num = 0;
let {signal, abort} = makeSignalAndAbort();
function makeSignalAndAbort() {
    const controller = new AbortController();
    const signal = controller.signal;
    const abort = controller.abort.bind(controller);
    return {
        signal,
        abort,
    };
}
async function onCountUpStart() {
    if (!countUpAct) {
        abort();
        ({signal, abort} = makeSignalAndAbort());
        await null;
    }
    try {
        countUpAct ??= await countup(countUp.valueAsNumber);
    } catch (reason) {
        error(reason.message);
    }
}
async function onClickAddition() {
    try {
        additionAct ??= addtion();
        await additionAct.add(additionNum.valueAsNumber);
        resultButton.disabled = false;
        result.innerText = "";
    } catch (reason) {
        error(reason?.message ?? 'error');
        resultButton.disabled = true;
    }
}
async function onClickAdditionResult() {
    if (!additionAct) return;
    try {
        await additionAct.close();
        additionAct = null;
        resultButton.disabled = true;
    } catch (reason) {
        error(reason?.message ?? 'error');
    }
}
async function countup(count) {
    const url = `/countup/${count}`;
    requestInfo(`url:${url} start.`)
    const response = await fetch(url, {
        method: "GET",
        headers: {
            "Accept": "application/json-seq",
            "Content-Type": "application/json-seq",
        },
        signal,
    });
    const readable2 = response.body.pipeThrough(new InputJsonSequenceStream());
    autoRead();
    return;
    async function autoRead() {
        try {
            for await (const item of readable2) {
                responseInfo(item);
            }
        } catch(reason) {
            error(reason?.message ?? 'error');
        }
        requestInfo(`url:${url} finish.`)
    }
}
function addtion() {
    const url = `/addition/`;
    requestInfo(`url:${url} starrt.`);
    const {readable:body, writable } = new OutputJsonSequenceStream()
    const writer = writable.getWriter();
    let isComplete = false;
    const complete = fetch(url, {
        method: "POST",
        headers: {
            "Accept": "application/json",
            "Content-Type": "application/json-seq",
        },
        signal,
        body,
        duplex: 'half',
    });
    return {
        add,
        close,
    }
    async function add(num) {
        if (isComplete) return;
        await writer.write(num);
        requestInfo(`add ${num}`);
    }
    async function close() {
        writer.close();
        const response = await complete;
        const resultValue = await response.text();;
        requestInfo(`= ${resultValue}`);
        result.innerText = resultValue;
        isComplete = true;
        requestInfo(`url:${url} finish.`);
    }
}
function requestInfo(value) {
    const info = templateRequest.content.firstChild.cloneNode(true);
    info.innerText = value;
    outputTarget.insertAdjacentElement("afterbegin", info);
}
function responseInfo(value) {
    const info = templateResponse.content.firstChild.cloneNode(true);
    info.innerText = value;
    outputTarget.insertAdjacentElement("afterbegin", info);
}
function error(value) {
    const info = templateError.content.firstChild.cloneNode(true);
    info.innerText = value;
    outputTarget.insertAdjacentElement("afterbegin", info);
}
</script>
<div>
<h2>application/json-seq output stream sample</h2>
<input type=number id=countUp value=0 />
<button id=countUpButton>click me.</button>
</div>
<div>
<h2>application/json-seq input stream sample</h2>
<input type=number id=additionNum value=0 disabled />
<button id=additionButton disabled>add</button>
<button id=resultButton disabled>result</button>
<span id=result></span>
</div>
<div>
<h2>output</h2>
<div id=outputTarget>
    <template id=templateRequest><div class="request"></div></template>
    <template id=templateResponse><div class="response"></div></template>
    <template id=templateError><div class="error"></div></template>
</div>
</div>
""", "text/html; charset=utf-8"));

app.MapGet("/countup/{count:int}", ([FromRoute]int count, CancellationToken cancellationToken) =>  {
    Log.LogStart(logger);
    return Results.Sequence(Enumerable(count, cancellationToken));
    static async IAsyncEnumerable<string> Enumerable(int count, [EnumeratorCancellation]CancellationToken cancellationToken)
    {
        for(var i = 0; i<= count; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            yield return $"{i}";
            
        }
    }
}).AddOpenApiOperationTransformer((operation, context, CancellationToken) =>
{
    operation.Summary = "Count up stream";
    operation.Description = "Returns a JSON Sequence stream of numbers";
    return Task.CompletedTask;
});
app.MapPost("/addition", async (Sequence<int> nums, CancellationToken cancellationToken) =>
{
    var addition = 0;
    await foreach(var num in nums.WithCancellation(cancellationToken))
    {
        addition += num;
    }
    return Results.Ok(addition);
}).AddOpenApiOperationTransformer((operation, context, CancellationToken) =>
{
    operation.Summary = "Addition stream";
    operation.Description = "Accepts a JSON Sequence stream of integers";
    return Task.CompletedTask;
});

app.MapOpenApi("/openapi/{documentName}.json");

await app.RunAsync();

static partial class Log
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "start"
    )]
    public static partial void LogStart(ILogger logger);
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "message: {message}"
    )]
    public static partial void LogMessage(ILogger logger, string message);
    
}


[JsonSerializable(typeof(Stream))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(IAsyncEnumerable<int>))]
[JsonSerializable(typeof(IAsyncEnumerable<string>))]
partial class JsonSchemaContext : JsonSerializerContext {}