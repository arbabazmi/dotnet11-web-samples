# CommissionMcp.Web: Project 2, part 2

A Blazor Web App (interactive server) where a user chats with a model that calls the tools of
[CommissionMcp.Server](../CommissionMcp.Server/README.md). Output streams into the page, each tool call and its result is
shown, and a write tool asks the user to approve it first. All data is synthetic.

Versions: .NET SDK 11.0.100, `net11.0`, `Microsoft.AspNetCore.Components.AI` **0.1.0-preview.1.26466.103** (experimental),
`ModelContextProtocol` 2.1.0, `Microsoft.Extensions.AI` 11.0.1, `Microsoft.Extensions.AI.OpenAI` 11.0.1.

## Run

Two terminals, from the repo root:

```powershell
dotnet run --project src/CommissionMcp.Server --urls http://localhost:5290
dotnet run --project src/CommissionMcp.Web --urls http://localhost:5005
```

Open http://localhost:5005. With no configuration the app uses the **Demo** provider, an offline stand-in for a model that
needs no key (see below). Try the example cards on the welcome screen, or type:

- `Summarize advisor A-102 for October` (read-only; two tool calls, no approval)
- `Reduce A-102 payout by $250 because of a duplicate trade` (write; pauses for Approve or Reject)

> **Do not add `--no-launch-profile` to the web app.** That flag skips `launchSettings.json`, which sets
> `ASPNETCORE_ENVIRONMENT=Development`. Without it the app runs as Production, ASP.NET Core stops serving the framework's
> static files, and `blazor.web.js` and the stylesheet return errors: the page loads but never becomes interactive and the
> log fills with `FileNotFoundException`. If you must skip the profile, set `$env:ASPNETCORE_ENVIRONMENT = "Development"`.

## Use a real model

The chat client is built from configuration (`Services/ChatClientFactory.cs`). Keys never go in the repository; use user
secrets or environment variables.

### Ollama (local, no key)

1. Install Ollama and pull a model that supports **tool calling**:

   ```powershell
   winget install --id Ollama.Ollama -e
   ollama pull qwen2.5:7b          # or llama3.1:8b
   ollama show qwen2.5:7b          # the "Capabilities" list must include: tools
   ```

   Ollama serves an OpenAI-compatible API at `http://localhost:11434/v1`.

2. Point the app at it:

   ```powershell
   cd src/CommissionMcp.Web
   dotnet user-secrets init
   dotnet user-secrets set "Chat:Provider" "Ollama"
   dotnet user-secrets set "Chat:Model" "qwen2.5:7b"
   ```

   `Chat:Endpoint` defaults to `http://localhost:11434/v1` and no API key is needed. The network timeout is 5 minutes,
   because a model running on a CPU can take a while to load and to answer.

3. Run the MCP server and the web app as above, then ask: `Summarize advisor A-102 for October`.

The model must support tool calling, otherwise it will answer in text and never call the MCP tools. Small models
sometimes pick the wrong tool or arguments; that is a property of the model, not of the approval flow.

### Other OpenAI-compatible endpoints

```powershell
dotnet user-secrets set "Chat:Provider" "OpenAI"
dotnet user-secrets set "Chat:Model" "<model name>"
dotnet user-secrets set "Chat:ApiKey" "<your key>"
dotnet user-secrets set "Chat:Endpoint" "<optional base URL>"
```

Environment variables work too (`Chat__Provider`, `Chat__Model`, `Chat__ApiKey`, `Chat__Endpoint`). The MCP server address
is `Mcp:Endpoint` (default `http://localhost:5290/mcp`).

Verified with `qwen2.5:7b` through Ollama on a CPU-only laptop (see "Live tests" below). Expect tens of seconds per turn.

## The Demo provider

`Services/ScriptedChatClient.cs` is an `IChatClient` that does no reasoning. It reads the question with a few patterns,
requests the matching MCP tool calls, and writes its answer from the tool results. It is used when no key is set and in the
tests. Everything around it is the real pipeline: the MCP client, the tool-calling middleware, the approval flow and the
Blazor AI components.

## The interface

- **Top bar**: which model is answering, that the MCP server is connected, and the three tools, with the one that needs
  approval marked.
- **Welcome cards**: three example questions; clicking one sends it.
- **Conversation**: your messages as bubbles, the assistant's answer streamed as plain text.
- **Tool calls**: one line each (tool, arguments, status); click to open the arguments and the result. A call the user
  declined shows "declined", not "done".
- **Approval card**: amber while waiting, with the tool name, the arguments (the amount shown as money), what it will do,
  and Approve and Reject buttons. It turns green or rose once decided. The view scrolls to a pending approval so the
  buttons are always visible (`wwwroot/chat.js`).
- Light and dark themes follow the system setting; the layout works down to phone width.

The chat shell, bubbles and input come from the AI package's own stylesheet (`ai-chat.css`), which the app must link
explicitly; `wwwroot/app.css` maps the app's colours onto its `--sc-ai-*` variables.

## How the pieces connect

```
Chat.razor ── UIAgent ── FunctionInvokingChatClient ── IChatClient (Demo or OpenAI-compatible)
                  │                  │
          FunctionApprovalBlock      └── tools from McpToolProvider ── MCP server (/mcp)
          (Approve / Reject)              read-only tools: plain      write tool: ApprovalRequiredAIFunction
```

- `Services/McpToolProvider.cs` connects once, lists the MCP tools, and wraps every tool that is **not** marked read-only
  (`readOnlyHint`) in `ApprovalRequiredAIFunction`. Approval is therefore driven by the server's annotations, not by tool names.
- `Services/ChatClientFactory.cs` builds the provider client and adds `UseFunctionInvocation()`.
- `Components/Pages/Chat.razor` creates a `UIAgent` with the tools and renders `ChatPage`, with custom `BlockRenderer`s for
  `FunctionApprovalBlock` (tool name, arguments, Approve and Reject buttons) and `FunctionInvocationContentBlock`
  (tool call and result).

## Findings

- **The AI components are experimental and early**: version `0.1.0-preview.1`, described as experimental in the release notes.
- **`UIAgent` does not run tools.** It is provider-neutral and only maps a streaming `IChatClient` into content blocks. Tool
  execution and approval requests come from `FunctionInvokingChatClient`, so the client you pass in must be built with
  `UseFunctionInvocation()`. The Microsoft docs did not show how MCP tools connect; the working path is
  `McpClientTool` (already an `AIFunction`) in `ChatOptions.Tools`, wrapped in `ApprovalRequiredAIFunction` when it needs approval.
- **`AgentContext.SendMessageAsync` completes only when the whole turn is finished**, including a pending approval. With a tool
  waiting for approval the conversation status is `AwaitingInput` and the call does not return until the user clicks
  Approve or Reject. Tests must start the send, wait for `AwaitingInput`, act, then await the send.
- **`FunctionApprovalBlock.Reject(string reason)`** takes a reason. On rejection the middleware gives the model a tool result
  saying the call was rejected (verified by the tests), so the model can tell the user nothing changed.
- **Two result shapes from MCP tools**: a successful call returns a `TextContent`; a failed call (the server threw an
  `McpException`) comes back as the raw MCP result JSON with `isError: true`.
- **Render mode**: a page-level `@rendermode InteractiveServer` with a static layout logged a Sections warning (the page
  title may stop updating after the page becomes interactive). Setting `@rendermode="InteractiveServer"` on `HeadOutlet` and
  `Routes` in `App.razor` removed it.
- **A mixed batch asks approval for everything.** When a real model requests a lookup and a change in one step, the framework
  asks approval for every call in the batch, including the read-only one. `AutoApprove.razor` approves read-only calls
  (by the server's annotation) so only the change waits for a click.
- **The package stylesheet must be linked** (`_content/Microsoft.AspNetCore.Components.AI/ai-chat.css`); without it the input is unstyled.
- **Preview package pulls RC dependencies.** It depends on `Microsoft.AspNetCore.Components.Web` 11.0.0-rc.1, which brings
  `Microsoft.Extensions.Validation` 11.0.0-rc.1 (a moderate advisory, GHSA-m28h-pj36-4364). The web app itself is not affected
  because the GA ASP.NET Core 11 framework supersedes it; the plain test project had to pin `Microsoft.Extensions.Validation` 11.0.0.

## Tests

```powershell
dotnet test tests/CommissionMcp.Web.Tests
```

Eleven tests: five configuration tests for the model providers, and six that drive the real `UIAgent` and `AgentContext` against the in-memory MCP server with the Demo client, no browser:
which tools need approval, a read-only question (calls shown, no approval), a write that pauses and changes nothing until
approved, a rejection (data unchanged, model told it was declined), an approved call the server refuses, and a follow-up request that reuses the advisor from an earlier turn.

## Live tests (opt-in)

Three extra tests run the same pipeline against a real model. They are skipped unless a provider is configured, so a normal
`dotnet test` never needs a model:

```powershell
$env:Chat__Provider = "Ollama"; $env:Chat__Model = "qwen2.5:7b"
dotnet test tests/CommissionMcp.Web.Tests --filter LiveModelTests
```

Result with `qwen2.5:7b` (CPU only): all three passed, 29 to 41 seconds each.

- **Read-only question**: the model called `get_advisor_summary` and answered with the right figures; no approval was asked.
- **Write request, approved**: the model called `adjust_payout(advisorId=A-102, amount=-250.5, ...)`, the turn paused, the
  data was unchanged until Approve, and then the adjustment was applied.
- **Write request, rejected**: data unchanged; the model told the user the request was declined.

Observations: a real model sends tool arguments as raw JSON values (`JsonElement`), not numbers; it may skip a tool the
demo model always calls (it did not call `get_statement` for the October summary); and it emits an empty text block before
its tool calls.

Assertions are deliberately loose (which tool, that the write pauses, that reject changes nothing), because a real model
is not deterministic.

## Not verified yet

- **Browser runs used Chrome, headless, driven by a script.** With the Demo model the full flow was checked (question, streaming,
  tool rows, approval, Approve, Reject, example cards, dark mode, phone width) with no console errors. With the real model
  (`qwen2.5:7b`) the answer and approval card were checked, and a change was approved and applied. Other browsers and manual
  use on a phone are untested.
- Only one real model has been tried: `qwen2.5:7b` through Ollama. Other models, and hosted ones such as OpenAI, have not.
- Auto-approval of read-only lookups in a mixed batch is implemented and the real-model run worked, but that run may not have
  produced a mixed batch, so the path has no test.
- The server has not been called from a different MCP client application such as the MCP Inspector.
