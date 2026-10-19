# Proof of concept: validation layer on the MCP transport

Demonstrates feasibility of the mechanism proposed in Section 6.3 of the
research design: a validation layer between an MCP client and the CarLo MCP
server that decides on every proposed tool call (task-scope policy) and logs
every call with its provenance. Scenario 1 of Section 6.4: a hidden instruction
in a processed document asks for a write; the write is blocked.

Deployment variant 1 (proxy on the transport, flow K7). The policy and the log
are separate classes so that the same code could be called from the function
layer of CarLo (variant 2, flows K4, K8, K14).

## Structure

| Project | Role in the architecture model |
|---|---|
| `MockCarLo/` | Stand-in for the CarLo MCP server and database: three tools (`GetTransportOrder`, `ReadDocument`, `UpdateCustomerBankAccount`), in-memory records, one inbound document with a hidden instruction. No validation on purpose. |
| `ValidationProxy/` | The validation layer. `PolicyDecisionPoint` (R5–R8), `ProvenanceLog` (R13, R14), `SessionState`, `ValidatedTool` (adapter that routes one upstream tool through policy and log), `Program.cs` (wiring, adds the `declare_task` tool). |
| `ScriptedClient/` | Stand-in for the customer's AI client on path B. Replays the call sequence an injected model would produce; no language model involved, so the run is deterministic. |

All three use the official C# SDK `ModelContextProtocol` 2.2.0 (Streamable HTTP).

## Run

Requires the .NET 10 SDK. Three terminals from this folder:

```
dotnet run --project MockCarLo                       # http://localhost:5101/mcp
dotnet run --project ValidationProxy                 # http://localhost:5100/mcp, writes provenance.jsonl
dotnet run --project ScriptedClient
```

Optional arguments: `ValidationProxy <listenUrl> <upstreamMcpUrl> <logPath>`,
`ScriptedClient <proxyMcpUrl>`.

## What the run shows

Run A, task `summarise-document`: `ReadDocument(DOC-4711)` is allowed and
returns a carrier e-mail whose hidden note asks the assistant to change customer
K-1001's IBAN. The client then issues that write. The proxy denies it: the tool
is outside the declared task. A read within the task still works.

Run B, task `maintain-customer`: the same write for K-1002 is allowed and
executed by the mock; `ReadDocument` is now the call outside scope. The policy
is about the task, not about writes as such.

Run C, no task declared: every call is denied (R8).

`provenance.jsonl` holds one line per call, allowed or denied, with session,
sequence number, declared task, arguments, decision, reason and the untrusted
sources that had entered the session before the call. The denied write in run A
carries `"untrusted_sources_before_call":["ReadDocument(DOC-4711)"]`, which is
the provenance an analyst would need.

## Findings while building

- Since protocol revision 2026-07-28 the SDK serves HTTP statelessly by
  default: no session id, and per-session configuration runs per request. A
  task declaration and provenance across calls need a session, so the proxy is
  configured stateful. A stateless deployment would have to carry the task
  scope in every request (for example in `_meta`), or the layer must sit where
  the session exists, which is an argument for variant 2.
- The proxy passes the upstream tool definitions through unchanged, including
  the `readOnlyHint`/`destructiveHint` annotations, but the policy does not use
  them: the specification treats annotations as untrusted, so the allow-list
  per task is the proxy's own.

## What it does not show

Effectiveness against a real model, performance, approval dialogs (R12), the
function-layer variant, and anything about CarLo itself. The task scopes are a
hard-coded table of two tasks.
