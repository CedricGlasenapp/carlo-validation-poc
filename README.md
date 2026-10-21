# Proof of concept: validation layer on the MCP transport

Companion code to the research design *Security Mechanisms for Agentic AI in
Enterprise Systems: Prompt Injection and Tool Abuse Mitigation* (Cedric
Glasenapp, TU Dresden, Chair of Business Information Systems, esp.
Intelligent Systems and Services, October 2026). The research design
prepares a diploma thesis with Soloplan GmbH on the transport management
system CarLo. Everything in this repository is a mock: the server, the
records, the document and the tool names are invented and contain nothing
of CarLo. The tag `forschungsdesign-2026-10` marks the state described in
Section 6.3 of the research design.

Demonstrates feasibility of the mechanism proposed in Section 6.3 of the
research design: a validation layer between an MCP client and the CarLo MCP
server that decides on every proposed tool call and logs every call with its
provenance. Two scenarios: a hidden instruction in a processed document asks
for a write outside the task (scenario 1, blocked by task scope), and the same
instruction inside a task that legitimately reads and writes (scenario 2,
blocked because the written value originates from the document).

Deployment variant 1 (proxy on the transport, flow K7). The policy and the log
are separate classes so that the same code could be called from the function
layer of CarLo (variant 2, flows K4, K8, K14).

## Structure

| Project | Role in the architecture model |
|---|---|
| `MockCarLo/` | Stand-in for the CarLo MCP server and database: three tools (`GetTransportOrder`, `ReadDocument`, `UpdateCustomerBankAccount`), in-memory records, one inbound document with a hidden instruction. No validation on purpose. |
| `ValidationProxy/` | The validation layer. `PolicyDecisionPoint` (R5, R6, R8, R9, R12), `ProvenanceLog` (R13, R14), `SessionState`, `ValidatedTool` (adapter that routes one upstream tool through policy and log), `Program.cs` (wiring; binds the task scope to the session). |
| `ScriptedClient/` | Stand-in for the customer's AI client on path B. Replays the call sequences an injected model would produce; no language model involved, so the runs are deterministic. |
| `sample-run/` | Output of one complete run: client console, provenance log, the writes the mock executed. |

All three projects use the official C# SDK `ModelContextProtocol` 2.2.0
(Streamable HTTP).

## Run

Requires the .NET 10 SDK. Three terminals from this folder:

```
dotnet run --project MockCarLo                       # http://localhost:5101/mcp
dotnet run --project ValidationProxy                 # http://localhost:5100/mcp, writes provenance.jsonl
dotnet run --project ScriptedClient
```

Optional arguments: `ValidationProxy <listenUrl> <upstreamMcpUrl> <logPath>`,
`ScriptedClient <proxyMcpUrl>`.

## How the layer decides

The task scope is bound to the session when it is opened, through the header
`X-Task-Scope`. The header stands in for a scope claim in the OAuth token of
path B: set by the host or the authorisation server on the user's behalf. The
model only ever produces tool calls, so nothing it proposes can widen the
scope. (A first iteration offered a `declare_task` tool instead; an injected
model could have called it, which is threat T7 exactly. Replaced.)

Rule 1, task scope: each task maps to the tools it may call. A call outside
the set is denied.

Rule 2, provenance of write arguments: once a tool classified as returning
third-party content has been called, a write whose protected parameter (here:
`iban`) carries a value found verbatim in that content is denied, because such
a write needs approval and the proxy has no approval channel. Values not found
in the untrusted content pass. The match is a normalised string comparison and
is a heuristic: a value the attacker reformats in the document, or has the
model transform, is not caught.

Both tables (task scopes, write tools, protected parameters) are hard-coded.
In a product they would be configuration; in CarLo most likely the import
configuration, which already says which fields a document may fill.

## What the runs show

Run A, task `summarise-document` (scenario 1): `ReadDocument(DOC-4711)`
returns a carrier e-mail whose hidden note asks the assistant to change
customer K-1001's IBAN. The client issues that write; the proxy denies it as
outside the task. A read within the task still works.

Run B, task `maintain-customer`: the same write for K-1002 is allowed and
executed; `ReadDocument` is now the call outside scope. The policy is about
the task, not about writes as such.

Run C, no task scope: every call is denied (R8).

Run D, task `process-inbound` (scenario 2): the background-agent case, reading
and writing in one task. The write with the IBAN from the document is denied
with the reason `value of 'iban' originates from untrusted content
ReadDocument(DOC-4711)`; the same write with an IBAN the user supplied is
executed.

`provenance.jsonl` holds one line per call, allowed or denied, with session,
sequence number, task, arguments, decision, reason, the untrusted sources that
had entered the session before the call, and a summary of the result. Reads
are logged as well as writes, which is the gap the case system has today (T13).

## Findings while building

- Since protocol revision 2026-07-28 the SDK serves HTTP statelessly by
  default: no session id, and per-session configuration runs per request. A
  task scope and provenance across calls need a session, so the proxy is
  configured stateful. A stateless deployment would have to carry scope and
  provenance in every request, or the layer must sit where the session
  exists, which is an argument for variant 2.
- The proxy passes the upstream tool definitions through unchanged, including
  the `readOnlyHint`/`destructiveHint` annotations, but the policy does not use
  them: the specification treats annotations as untrusted, so the
  classification of writes is the proxy's own.
- Intent must be bound to a channel the model cannot write to. A tool call is
  not such a channel; a token scope or a host-side setting is.

## What it does not show

Effectiveness against a real model, performance, an approval dialog (R12; the
layer denies where a product would ask), the function-layer variant, and
anything about CarLo itself.
