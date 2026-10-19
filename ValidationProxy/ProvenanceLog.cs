using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValidationProxy;

/// <summary>
/// Append-only log of every tool call that reached the validation layer,
/// allowed or denied, with the provenance the policy saw at that moment
/// (requirements R13, R14). One JSON object per line. Reads are logged as
/// well as writes, which is the gap the case system has today (T13).
/// </summary>
public sealed class ProvenanceLog(string path)
{
    public sealed record Entry(
        [property: JsonPropertyName("ts")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("session")] string SessionId,
        [property: JsonPropertyName("seq")] int Sequence,
        [property: JsonPropertyName("task")] string? DeclaredTask,
        [property: JsonPropertyName("tool")] string Tool,
        [property: JsonPropertyName("args")] IDictionary<string, JsonElement>? Arguments,
        [property: JsonPropertyName("decision")] string Decision,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("untrusted_sources_before_call")] IReadOnlyList<string> UntrustedSourcesBeforeCall,
        [property: JsonPropertyName("result")] string? ResultSummary);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly Lock _gate = new();

    public string Path { get; } = path;

    public void Append(Entry entry)
    {
        var line = JsonSerializer.Serialize(entry, Options);
        lock (_gate)
        {
            File.AppendAllText(Path, line + Environment.NewLine);
        }
        Console.WriteLine($"[proxy] {entry.Decision,-7} #{entry.Sequence} {entry.Tool} task={entry.DeclaredTask ?? "-"} untrusted={entry.UntrustedSourcesBeforeCall.Count} :: {entry.Reason}");
    }
}
