using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ValidationProxy;

/// <summary>
/// One upstream tool as the proxy presents it to the client. The tool
/// definition (name, description, input schema, annotations) is passed
/// through unchanged; only the invocation is routed through the policy
/// decision point and the provenance log before it reaches the real server.
/// This class is the adapter; the mechanism lives in the two classes it calls.
/// </summary>
public sealed class ValidatedTool(
    McpClientTool upstreamTool,
    McpClient upstream,
    SessionState session,
    PolicyDecisionPoint policy,
    ProvenanceLog log) : McpServerTool
{
    public override Tool ProtocolTool { get; } = upstreamTool.ProtocolTool;

    public override IReadOnlyList<object> Metadata { get; } = [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var name = ProtocolTool.Name;
        var arguments = request.Params?.Arguments;
        var sourcesBefore = session.UntrustedSources;
        var seq = ++session.CallCounter;

        var decision = policy.Decide(session, name, arguments);

        if (!decision.Allowed)
        {
            log.Append(new(DateTimeOffset.UtcNow, session.SessionId, seq, session.DeclaredTask, name,
                arguments, "DENIED", decision.Reason, sourcesBefore, null));

            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = $"blocked by validation layer: {decision.Reason}" }],
            };
        }

        var result = await upstream.CallToolAsync(
            new CallToolRequestParams { Name = name, Arguments = arguments }, cancellationToken);

        if (PolicyDecisionPoint.ReturnsUntrustedContent(name))
        {
            var id = arguments is not null && arguments.TryGetValue("documentId", out var d) ? d.ToString() : "?";
            var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            session.UntrustedContent.Add(new($"{name}({id})", text));
        }

        log.Append(new(DateTimeOffset.UtcNow, session.SessionId, seq, session.DeclaredTask, name,
            arguments, "ALLOWED", decision.Reason, sourcesBefore, Summarise(result)));

        return result;
    }

    private static string Summarise(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
        text = text.ReplaceLineEndings(" ");
        return (result.IsError == true ? "error: " : "") + (text.Length > 80 ? text[..80] + "..." : text);
    }
}
