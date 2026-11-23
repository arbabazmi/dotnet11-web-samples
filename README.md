# .NET 11 Web Samples

Three small ASP.NET Core 11 projects that try what is new on the web side of .NET 11, and write down what worked, what
surprised me and what did not. All data is synthetic. Everything here was built and run on .NET SDK 11.0.100.

| # | Project | Folder | What it shows |
| --- | --- | --- | --- |
| 1 | Fee calculator API | [src/FeeCalc.Api](src/FeeCalc.Api) | C# 15 `union` types, mapped to HTTP with exhaustive `switch` |
| 2 | MCP server + Blazor AI chat | [src/CommissionMcp.Server](src/CommissionMcp.Server), [src/CommissionMcp.Web](src/CommissionMcp.Web) | An MCP server, and a chat page where a model calls its tools and a person approves any change |
| 3 | Native AOT benchmark | [src/Aot.Bench](src/Aot.Bench) | Cold start, memory and image size: JIT vs Native AOT on .NET 10 and .NET 11 |

Each project folder has its own README with the details, commands and findings. This page is the overview.

## Prerequisites

| Needed for | Install |
| --- | --- |
| Everything | [.NET SDK 11.0.100](https://dotnet.microsoft.com/download/dotnet/11.0) (pinned in `global.json`) |
| Project 3 | Docker Desktop, running |
| Project 2 with a real model (optional) | [Ollama](https://ollama.com/download), then `ollama pull qwen2.5:7b` |
| Browser checks (optional) | Node.js and a Chromium-based browser, only if you script a browser yourself |

Project 2 works with no model at all: it ships an offline **Demo** model that reads a few phrases and calls the tools.

```powershell
git clone https://github.com/arbabazmi/dotnet11-web-samples
cd dotnet11-web-samples
dotnet test          # all unit and integration tests; the 3 live-model tests are skipped
```

## Quick start

**Project 1: fee calculator**

```powershell
dotnet run --project src/FeeCalc.Api --urls http://localhost:5199
# open http://localhost:5199/openapi/v1.json
```

**Project 2: MCP server and chat** (two terminals)

```powershell
# terminal 1
dotnet run --project src/CommissionMcp.Server --urls http://localhost:5290
# terminal 2
dotnet run --project src/CommissionMcp.Web --urls http://localhost:5005
# open http://localhost:5005
```

Do **not** add `--no-launch-profile` to the web command. That runs the app in the Production environment, which does not
serve the page's scripts and styles. To use a local model, see [src/CommissionMcp.Web/README.md](src/CommissionMcp.Web/README.md).

**Project 3: benchmark** (Docker running)

```powershell
./bench/bench.ps1 -Runs 3 -Requests 20     # quick check
./bench/bench.ps1                          # 30 cold runs per variant
```

## What is new in .NET 11 here, and what I found

### 1. C# 15 unions in an API ([details](src/FeeCalc.Api/README.md))

`public union CalcResult(Calculated, RateNotFound, ValidationFailed);` declares a value that is exactly one of a fixed list of
types. A `switch` over it with no default arm lets the compiler check that every case maps to an HTTP status.

- Unions compiled on SDK 11.0.100 with no preview switch.
- A missing case is only a **warning** (CS8509) by default. The project promotes it with `<WarningsAsErrors>CS8509</WarningsAsErrors>`.
  The captured compiler message is in [docs/articles/compiler-diagnostic.txt](docs/articles/compiler-diagnostic.txt).
- A union serialises as just the case that was picked, with no marker of which one, so every case carries a `kind` property.
- OpenAPI 3.2 is emitted. Handlers return `IResult` after `ToHttp()`, so the generator cannot see the union; the cases appear
  only because each status is declared with `.Produces<T>()`.
- 15 tests.

### 2. MCP server and Blazor AI components ([server](src/CommissionMcp.Server/README.md), [web](src/CommissionMcp.Web/README.md))

- `dotnet new mcpserver -t remote` gives a working stateless HTTP MCP server (`ModelContextProtocol.AspNetCore` 2.1.0).
  Three tools over fictional commission data: two read-only, and `adjust_payout`, which changes data.
- The tools carry MCP annotations (`readOnlyHint`), and the web app wraps every non-read-only tool in
  `ApprovalRequiredAIFunction`. No tool names are hard-coded.
- The Blazor AI components (`Microsoft.AspNetCore.Components.AI` 0.1.0-preview.1, experimental) draw the chat and the approval
  block. They do **not** run tools: `UseFunctionInvocation()` from Microsoft.Extensions.AI does.
- `SendMessageAsync` returns only when the whole turn ends, including a pending approval, so tests start the send, wait for
  `AwaitingInput`, act, then await the send.
- A real model may ask for a lookup and a change in one step, and the framework then asks approval for the whole batch.
  Read-only lookups are auto-approved so only the change waits for a click.
- 32 tests (21 server, 11 web), plus 3 opt-in live-model tests.

### 3. Native AOT vs JIT ([details and raw data](src/Aot.Bench/README.md))

One Minimal API, four publishes, 30 cold starts each, in Docker on one laptop (medians):

| Variant | Ready (ms) | Peak memory (MB) | Image (MB) |
| --- | ---: | ---: | ---: |
| .NET 10 JIT | 548.9 | 64.8 | 234.4 |
| .NET 10 AOT | 236.8 | 19.1 | 159.6 |
| .NET 11 JIT | 534.9 | 66.9 | 246.2 |
| .NET 11 AOT | 249.1 | 20.2 | 174.4 |

AOT started about 2.1 to 2.3 times sooner and used about a third of the memory. The .NET 10 vs .NET 11 difference is inside the
run-to-run noise, so these results do not show .NET 11 starting faster or slower. Local Docker only; no cloud run was made.

## What is and is not verified

| Verified | Not verified |
| --- | --- |
| Projects 1 and 2 server: automated tests pass | Project 3 numbers come from one laptop, variants run one after another |
| Project 2 chat: headless tests with the Demo model | Only one real model tried: `qwen2.5:7b` through Ollama |
| Project 2 chat in Chrome (headless) with the Demo model: question, approval, Approve, Reject, dark mode, phone width | Other browsers, hosted models, and a separate MCP client such as the MCP Inspector |
| `qwen2.5:7b`: 3 live headless tests passed; in Chrome it answered and a change was approved and applied | Auto-approval of a mixed batch has no automated test |
| AOT publish: no warnings on .NET 10 or .NET 11 | Debugging cost and library compatibility of AOT were not measured |

The Blazor AI package is a preview (0.1.0-preview.1). Names and behaviour may change.

## Troubleshooting

- **Build fails with "file is locked by CommissionMcp.Server/Web".** A copy of the app is still running. Stop it
  (Ctrl+C in its window, or `Stop-Process -Id <pid>`) and build again.
- **Web page shows "Can't reach the MCP server".** Start the server (terminal 1 above), then click **Try again**.
- **`FileNotFoundException` for `blazor.web.js` or `styles.css`.** The app is running in Production. Start it without
  `--no-launch-profile`, or set `ASPNETCORE_ENVIRONMENT=Development`.
- **Project 3 will not start.** Docker must be running.

## Repository layout

```text
src/FeeCalc.Api              Project 1
src/CommissionMcp.Server     Project 2: MCP server
src/CommissionMcp.Web        Project 2: Blazor chat
src/Aot.Bench                Project 3
bench/                       Project 3 Dockerfile, script and results
tests/                       xUnit tests for projects 1 and 2
docs/articles/               captured compiler diagnostic
global.json                  pins SDK 11.0.100
```
