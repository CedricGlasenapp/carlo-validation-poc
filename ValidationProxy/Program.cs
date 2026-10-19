// Validation layer as a proxy on the MCP transport (deployment variant 1 in
// Section 6.3). Towards the client it is an MCP server; towards the real
// server it is an MCP client. Every tools/call passes the policy decision
// point and the provenance log before it is forwarded. The proxy sees flow K7
// only; the same PolicyDecisionPoint and ProvenanceLog could be called from
// the function layer of the case system, which would cover K4, K8 and K14.

using System.ComponentModel;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ValidationProxy;

var listenUrl = args.Length > 0 ? args[0] : "http://localhost:5100";
var upstreamUrl = args.Length > 1 ? args[1] : "http://localhost:5101/mcp";
var logPath = args.Length > 2 ? args[2] : "provenance.jsonl";

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
        options.ServerInfo = new() { Name = "carlo-validation-proxy", Version = "0.1" };
    })
    .WithHttpTransport(http =>
    {
        // Since protocol revision 2026-07-28 the SDK serves HTTP statelessly by
        // default: no session id, and ConfigureSessionOptions runs per request.
        // A task declaration and provenance across calls need a session, so the
        // proxy runs stateful. (A stateless deployment would have to carry the
        // task scope in every request instead; see README.)
        http.SessionMode = HttpServerSessionMode.Stateful;

        // Each client session gets its own state and its own wrapped tool set.
        http.ConfigureSessionOptions = (_, options, _) =>
        {
            var session = new SessionState();
            var tools = new McpServerPrimitiveCollection<McpServerTool>
            {
                DeclareTaskTool(session, log),
            };
            foreach (var tool in upstreamTools)
                tools.Add(new ValidatedTool(tool, upstream, session, policy, log));

            options.ToolCollection = tools;
            Console.WriteLine($"proxy: new session {session.SessionId}");
            return Task.CompletedTask;
        };
    });

var app = builder.Build();
app.MapMcp("/mcp");
Console.WriteLine($"proxy listening on {listenUrl}/mcp, log at {Path.GetFullPath(logPath)}");
app.Run(listenUrl);

// The one tool the proxy adds itself: the intent declaration of R8. Without it
// no other tool is callable in the session.
static McpServerTool DeclareTaskTool(SessionState session, ProvenanceLog log) =>
    McpServerTool.Create(
        ([Description("Task the client is working on")] string task) =>
        {
            var known = PolicyDecisionPoint.KnownTasks.Contains(task);
            if (known) session.DeclaredTask = task;
            log.Append(new(DateTimeOffset.UtcNow, session.SessionId, ++session.CallCounter, session.DeclaredTask,
                "declare_task", null, known ? "ALLOWED" : "DENIED",
                known ? $"task scope set to '{task}'" : $"unknown task '{task}'",
                session.UntrustedSources.ToArray(), null));
            return known ? $"task scope set to '{task}'" : $"error: unknown task '{task}'";
        },
        new McpServerToolCreateOptions
        {
            Name = "declare_task",
            Description = "Declares the task scope for this session; required before any other tool (R8). "
                          + "Known tasks: " + string.Join(", ", PolicyDecisionPoint.KnownTasks) + ".",
        });
