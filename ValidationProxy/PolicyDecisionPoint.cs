using System.Text.Json;

namespace ValidationProxy;

/// <summary>
/// Decides whether a proposed tool call may be executed (requirements R5 to R8).
/// The only policy implemented is task scope: a client declares the task it is
/// working on, and each task maps to the set of tools it may use. A call to a
/// tool outside that set is denied, whatever text in the context asked for it.
/// The policy uses its own table, not the server's tool annotations, because
/// the MCP specification treats annotations as untrusted hints.
/// </summary>
public sealed class PolicyDecisionPoint
{
    public sealed record Decision(bool Allowed, string Reason);

    /// <summary>Task scope → tools the task may call. Would be configuration in a product.</summary>
    private static readonly Dictionary<string, HashSet<string>> TaskScopes = new()
    {
        ["summarise-document"] = ["ReadDocument", "GetTransportOrder"],
        ["maintain-customer"] = ["GetTransportOrder", "UpdateCustomerBankAccount"],
    };

    /// <summary>Tools whose results bring third-party text into the context (boundary TB3 carrying TB1 content).</summary>
    private static readonly HashSet<string> UntrustedContentTools = ["ReadDocument"];

    public static IEnumerable<string> KnownTasks => TaskScopes.Keys;

    public Decision Decide(SessionState session, string tool, IDictionary<string, JsonElement>? arguments)
    {
        if (session.DeclaredTask is null)
            return new(false, "no task declared; call declare_task first (R8)");

        if (!TaskScopes.TryGetValue(session.DeclaredTask, out var allowed))
            return new(false, $"unknown task '{session.DeclaredTask}'");

        if (!allowed.Contains(tool))
            return new(false, $"tool '{tool}' is outside the scope of task '{session.DeclaredTask}' (R5, R6)");

        return new(true, $"tool '{tool}' is within the scope of task '{session.DeclaredTask}'");
    }

    public static bool ReturnsUntrustedContent(string tool) => UntrustedContentTools.Contains(tool);
}
