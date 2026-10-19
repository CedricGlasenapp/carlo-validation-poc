namespace ValidationProxy;

/// <summary>
/// What the validation layer knows about one client session. It is the
/// minimum the policy needs: the task scope bound to the session when it was
/// opened (R8) and the untrusted content that has entered the session so far,
/// so that every later call can be checked and logged against it (R6, R13).
/// </summary>
public sealed class SessionState
{
    public sealed record UntrustedItem(string Source, string Text);

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Task scope the session was opened with. It comes from the connection
    /// (header, in a product the OAuth scope), never from a tool call, so that
    /// nothing the model proposes can widen it.
    /// </summary>
    public string? DeclaredTask { get; set; }

    /// <summary>Third-party content that entered the context in this session, in order.</summary>
    public List<UntrustedItem> UntrustedContent { get; } = [];

    public IReadOnlyList<string> UntrustedSources => UntrustedContent.Select(u => u.Source).ToArray();

    public int CallCounter { get; set; }
}
