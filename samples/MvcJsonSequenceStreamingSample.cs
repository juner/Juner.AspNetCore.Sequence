#!/usr/local/share/dotnet/dotnet run
#:sdk Microsoft.NET.Sdk.Web
#:project ../Sequence/Juner.AspNetCore.Sequence.csproj
#:property UserSecretsId=DaiwaHouseUketsuke.Web       
#:property RootNamespace=Juner.AspNetCore.Sequence.Sample.MvcJsonSequenceStreamingSample
#:property TargetFramework=net10.0
#:property TargetFrameworks=net10.0
#:property PublishAot=false
#:package Microsoft.AspNetCore.OpenApi@10.0.5

using Juner.AspNetCore.Sequence.Http;
using Juner.AspNetCore.Sequence.Http.HttpResults;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options
 => options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "MVC JSON Sequence Streaming Sample";
        return Task.CompletedTask;
    }))
    .AddSequenceOpenApi();

builder.Services.AddLogging(
    static builder =>
    {
        builder.AddSimpleConsole(options =>
            options.IncludeScopes = true
        );
        builder.AddFilter(level => true);
    }
);

builder.Services
    .AddControllers()
    .AddSequenceFormatter();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.MapOpenApi("/openapi/{documentName}.json");

app.MapControllers();

await app.RunAsync();

namespace Juner.AspNetCore.Sequence.Sample.MvcJsonSequenceStreamingSample
{
    [ApiController]
    [Route("/")]
    public partial class MainController(ILogger<MainController> logger) : ControllerBase
    {
        [HttpGet("")]
        [Produces("text/html", Type = typeof(string))]
        public IResult Index() => Results.Content($$"""
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
                const resultValue = await response.text();
                responseInfo(`${resultValue}`);
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
        """, "text/html; charset=utf-8");

        /// <summary>
        /// 
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        [HttpGet("countup/{count:int}")]
        [EndpointSummary("Count up stream")]
        [EndpointDescription("Returns a JSON Sequence stream of numbers")]
        public JsonSequenceResult<string> Countup([FromRoute] int count)
        {
            LogStart();
            return TypedResults.JsonSequence(Enumerable(count, HttpContext.RequestAborted));
            static async IAsyncEnumerable<string> Enumerable(int count, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                for (var i = 0; i <= count; i++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                    yield return $"{i}";

                }
            }
        }

        /// <summary>
        /// Addition stream <br/>
        /// note: use <q>[Consumes(typeof(IAsyncEnumerable&lt;T&gt;), "application/json-seq"]</q> is broken openapi type
        /// </summary>
        /// <param name="nums"></param>
        /// <returns></returns>
        [HttpPost("addition")]
        [EndpointSummary("Addition stream")]
        [EndpointDescription("Accepts a JSON Sequence stream of integers")]
        [Consumes(typeof(IAsyncEnumerable<int>), "application/json-seq")]
        public async Task<Ok<int>> Addition(Sequence<int> nums)
        {
            var addition = 0;
            await foreach (var num in nums.WithCancellation(HttpContext.RequestAborted))
            {
                addition += num;
            }
            return TypedResults.Ok(addition);
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "start"
        )]
        partial void LogStart();

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "message: {message}"
        )]
        partial void LogMessage(string message);
    }
}
