namespace ValidationProxy;

/// <summary>
/// What the validation layer knows about one client session. It is the
/// minimum the policy needs: the task the client declared (R8) and which
/// untrusted content has entered the session so far, so that every later call
/// can be logged with that provenance (R13, R14).
/// </summary>
public sealed class SessionState
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Task scope declared by the client; null until declare_task was called.</summary>
    public string? DeclaredTask { get; set; }

    /// <summary>Identifiers of untrusted content that entered the context in this session, in order.</summary>
    public List<string> UntrustedSources { get; } = [];

    public int CallCounter { get; set; }
}
