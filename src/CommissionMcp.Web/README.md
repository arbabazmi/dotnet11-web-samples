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
needs no key (see below). Try:

- `Summarize advisor A-102 for October` (read-only; two tool calls, no approval)
- `Reduce A-102 payout by $250 because of a duplicate trade` (write; pauses for Approve or Reject)

## Use a real model

The chat client is built from configuration. Any OpenAI-compatible endpoint works (OpenAI, Azure OpenAI, Ollama, and others).
The key never goes in the repository; keep it in user secrets or an environment variable.

```powershell
cd src/CommissionMcp.Web
dotnet user-secrets set "Chat:Provider" "OpenAI"
dotnet user-secrets set "Chat:Model" "<model name>"
dotnet user-secrets set "Chat:ApiKey" "<your key>"
dotnet user-secrets set "Chat:Endpoint" "<optional base URL, e.g. http://localhost:11434/v1 for Ollama>"
```

Environment variables work too (`Chat__Provider`, `Chat__Model`, `Chat__ApiKey`, `Chat__Endpoint`). The MCP server address is
`Mcp:Endpoint` (default `http://localhost:5290/mcp`). **A real model has not been tried yet**; only the Demo provider has run.

## The Demo provider

`Services/ScriptedChatClient.cs` is an `IChatClient` that does no reasoning. It reads the question with a few patterns,
requests the matching MCP tool calls, and writes its answer from the tool results. It is used when no key is set and in the
tests. Everything around it is the real pipeline: the MCP client, the tool-calling middleware, the approval flow and the
Blazor AI components.

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

## Findings (for the article)

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
- **Preview package pulls RC dependencies.** It depends on `Microsoft.AspNetCore.Components.Web` 11.0.0-rc.1, which brings
  `Microsoft.Extensions.Validation` 11.0.0-rc.1 (a moderate advisory, GHSA-m28h-pj36-4364). The web app itself is not affected
  because the GA ASP.NET Core 11 framework supersedes it; the plain test project had to pin `Microsoft.Extensions.Validation` 11.0.0.

## Tests

```powershell
dotnet test tests/CommissionMcp.Web.Tests
```

Five tests drive the real `UIAgent` and `AgentContext` against the in-memory MCP server with the Demo client, no browser:
which tools need approval, a read-only question (calls shown, no approval), a write that pauses and changes nothing until
approved, a rejection (data unchanged, model told it was declined), and an approved call the server refuses.

## Not verified yet

- The page was served and rendered over HTTP (title, welcome text, input box), but I have **not clicked through the
  interactive UI in a browser**: streaming in the page and the Approve and Reject buttons are covered by the headless tests
  that use the same `UIAgent` and blocks, not by a browser test.
- No real language model has been used.
- The second screenshot for the article (calling the MCP server from a different MCP client) has not been taken.
