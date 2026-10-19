using System.Text.Json;
using System.Text.RegularExpressions;

namespace ValidationProxy;

/// <summary>
/// Decides whether a proposed tool call may be executed. Two rules:
///
/// 1. Task scope (R5, R8): the session is bound to a task, and each task maps
///    to the tools it may use. A call outside that set is denied, whatever
///    text in the context asked for it.
///
/// 2. Provenance of write arguments (R6, R9, R12): once untrusted content has
///    entered the session, a write whose protected parameter carries a value
///    taken verbatim from that content is not executed on the proposal alone.
///    In a product it would go to approval; this layer has no approval
///    channel and therefore denies it. Values that are not in the untrusted
///    content, a bank account the user typed, pass.
///
/// The policy uses its own tables, not the server's tool annotations, because
/// the MCP specification treats annotations as untrusted hints. In a product
/// the tables would be configuration, in CarLo most likely the import
/// configuration that already says which fields a document may fill.
/// </summary>
public sealed partial class PolicyDecisionPoint
{
    public sealed record Decision(bool Allowed, string Reason);

    /// <summary>Task scope → tools the task may call.</summary>
    private static readonly Dictionary<string, HashSet<string>> TaskScopes = new()
    {
        ["summarise-document"] = ["ReadDocument", "GetTransportOrder"],
        ["maintain-customer"] = ["GetTransportOrder", "UpdateCustomerBankAccount"],
        ["process-inbound"] = ["ReadDocument", "GetTransportOrder", "UpdateCustomerBankAccount"],
    };

    /// <summary>Tools whose results bring third-party text into the context (TB3 carrying TB1 content).</summary>
    private static readonly HashSet<string> UntrustedContentTools = ["ReadDocument"];

    /// <summary>Tools that change records, by the proxy's own classification.</summary>
    private static readonly HashSet<string> WriteTools = ["UpdateCustomerBankAccount"];

    /// <summary>Write tool → parameters whose value must not originate from untrusted content.</summary>
    private static readonly Dictionary<string, string[]> ProtectedParameters = new()
    {
        ["UpdateCustomerBankAccount"] = ["iban"],
    };

    private const int MinimumMatchLength = 6;

    public static IEnumerable<string> KnownTasks => TaskScopes.Keys;

    public static bool ReturnsUntrustedContent(string tool) => UntrustedContentTools.Contains(tool);

    public Decision Decide(SessionState session, string tool, IDictionary<string, JsonElement>? arguments)
    {
        // Rule 1: task scope.
        if (session.DeclaredTask is null)
            return new(false, "no task scope bound to this session (R8)");

        if (!TaskScopes.TryGetValue(session.DeclaredTask, out var allowed))
            return new(false, $"unknown task '{session.DeclaredTask}'");

        if (!allowed.Contains(tool))
            return new(false, $"tool '{tool}' is outside the scope of task '{session.DeclaredTask}' (R5)");

        // Rule 2: provenance of write arguments.
        if (WriteTools.Contains(tool) && session.UntrustedContent.Count > 0)
        {
            foreach (var parameter in ProtectedParameters.GetValueOrDefault(tool, []))
            {
                if (arguments is null || !arguments.TryGetValue(parameter, out var element))
                    continue;

                var source = FindInUntrustedContent(session, element.ToString());
                if (source is not null)
                    return new(false, $"value of '{parameter}' originates from untrusted content {source}; "
                                      + "a write with that value needs approval, which this layer cannot obtain (R6, R12)");
            }

            return new(true, $"write after untrusted content; protected values not taken from it (R6)");
        }

        return new(true, $"tool '{tool}' is within the scope of task '{session.DeclaredTask}'");
    }

    /// <summary>
    /// Verbatim match after normalisation (letters and digits only, lower
    /// case). A heuristic: a value the attacker reformats in the document, or
    /// has the model transform, is not caught. Stated as such in the thesis.
    /// </summary>
    private static string? FindInUntrustedContent(SessionState session, string value)
    {
        var needle = Normalise(value);
        if (needle.Length < MinimumMatchLength)
            return null;

        return session.UntrustedContent
            .FirstOrDefault(item => Normalise(item.Text).Contains(needle))?.Source;
    }

    private static string Normalise(string text) => NonAlphanumeric().Replace(text, "").ToLowerInvariant();

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
