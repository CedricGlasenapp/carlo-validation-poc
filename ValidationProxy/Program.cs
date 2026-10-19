// Validation layer as a proxy on the MCP transport (deployment variant 1 in
// Section 6.3). Towards the client it is an MCP server; towards the real
// server it is an MCP client. Every tools/call passes the policy decision
// point and the provenance log before it is forwarded. The proxy sees flow K7
// only; the same PolicyDecisionPoint and ProvenanceLog could be called from
// the function layer of the case system, which would cover K4, K8 and K14.

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ValidationProxy;

var listenUrl = args.Length > 0 ? args[0] : "http://localhost:5100";
var upstreamUrl = args.Length > 1 ? args[1] : "http://localhost:5101/mcp";
var logPath = args.Length > 2 ? args[2] : "provenance.jsonl";

// Header that carries the task scope when a session is opened. It stands in
// for a scope claim in the OAuth token of path B: set by the host or the
// authorisation server on the user's behalf, not writable by the model, which
// only ever produces tool calls.
const string TaskScopeHeader = "X-Task-Scope";

// One connection to the real server; its tool list is what the proxy re-exposes.
var upstream = await McpClient.CreateAsync(new HttpClientTransport(new()
{
    Endpoint = new Uri(upstreamUrl),
    Name = "upstream",
}));
var upstreamTools = await upstream.ListToolsAsync();
Console.WriteLine($"proxy: upstream {upstreamUrl} exposes {upstreamTools.Count} tools: "
                  + string.Join(", ", upstreamTools.Select(t => t.Name)));

var policy = new PolicyDecisionPoint();
var log = new ProvenanceLog(logPath);

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "carlo-validation-proxy", Version = "0.2" };
    })
    .WithHttpTransport(http =>
    {
        // Since protocol revision 2026-07-28 the SDK serves HTTP statelessly by
        // default: no session id, and ConfigureSessionOptions runs per request.
        // A task scope and provenance across calls need a session, so the
        // proxy runs stateful. (A stateless deployment would have to carry the
        // scope and the provenance in every request instead; see README.)
        http.SessionMode = HttpServerSessionMode.Stateful;

        // Each client session gets its own state and its own wrapped tool set.
        http.ConfigureSessionOptions = (httpContext, options, _) =>
        {
            var session = new SessionState
            {
                DeclaredTask = httpContext.Request.Headers[TaskScopeHeader].FirstOrDefault(),
            };

            var tools = new McpServerPrimitiveCollection<McpServerTool>();
            foreach (var tool in upstreamTools)
                tools.Add(new ValidatedTool(tool, upstream, session, policy, log));

            options.ToolCollection = tools;
            Console.WriteLine($"proxy: new session {session.SessionId}, task scope '{session.DeclaredTask ?? "-"}'");
            return Task.CompletedTask;
        };
    });

var app = builder.Build();
app.MapMcp("/mcp");
Console.WriteLine($"proxy listening on {listenUrl}/mcp, log at {Path.GetFullPath(logPath)}");
app.Run(listenUrl);
