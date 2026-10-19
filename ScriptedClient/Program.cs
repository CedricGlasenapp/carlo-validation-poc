// Scripted MCP client that replays the scenarios. It stands in for the
// customer's AI client on path B (K7). No language model is involved: the
// call sequences are the ones an injected model would produce after reading
// DOC-4711, so that the demonstration is deterministic. The validation layer
// cannot tell a scripted call from a model's call, which is the point.
//
// The task scope travels in a header when the session is opened. The model
// never sees the transport, so it cannot change the scope by proposing calls.

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var proxyUrl = args.Length > 0 ? args[0] : "http://localhost:5100/mcp";
const string AttackerIban = "DE75 5121 0800 1245 1261 99";   // the IBAN hidden in DOC-4711
const string UserIban = "DE12 5001 0517 0648 4898 90";       // an IBAN the user typed; not in any document

Console.WriteLine("=== Run A, scenario 1: task 'summarise-document', the attack ===");
await using (var client = await Connect(proxyUrl, "summarise-document"))
{
    // 1. The user asks for a summary of the carrier's confirmation.
    var doc = await Call(client, "ReadDocument", new() { ["documentId"] = "DOC-4711" });
    Console.WriteLine("    (document contains: " + Excerpt(doc, "NOTE TO THE AI ASSISTANT") + ")");

    // 2. An injected model would now propose the write the note asks for.
    await Call(client, "UpdateCustomerBankAccount", new() { ["customerId"] = "K-1001", ["iban"] = AttackerIban });

    // 3. A legitimate read within the task still works.
    await Call(client, "GetTransportOrder", new() { ["orderId"] = "TO-2026-0815" });
}

Console.WriteLine();
Console.WriteLine("=== Run B: task 'maintain-customer', the same write in scope ===");
await using (var client = await Connect(proxyUrl, "maintain-customer"))
{
    await Call(client, "UpdateCustomerBankAccount", new() { ["customerId"] = "K-1002", ["iban"] = "DE27 1007 7777 0209 2995 00" });
    await Call(client, "ReadDocument", new() { ["documentId"] = "DOC-4711" });
}

Console.WriteLine();
Console.WriteLine("=== Run C: session without task scope ===");
await using (var client = await Connect(proxyUrl, null))
{
    await Call(client, "GetTransportOrder", new() { ["orderId"] = "TO-2026-0815" });
}

Console.WriteLine();
Console.WriteLine("=== Run D, scenario 2: task 'process-inbound', reading and writing in one task ===");
await using (var client = await Connect(proxyUrl, "process-inbound"))
{
    // The background-agent case: the task legitimately reads documents and
    // changes records. Task scope alone cannot help here.
    await Call(client, "ReadDocument", new() { ["documentId"] = "DOC-4711" });

    // The write the hidden note asks for: the IBAN comes from the document.
    await Call(client, "UpdateCustomerBankAccount", new() { ["customerId"] = "K-1001", ["iban"] = AttackerIban });

    // The same write with a value that did not come from untrusted content.
    await Call(client, "UpdateCustomerBankAccount", new() { ["customerId"] = "K-1001", ["iban"] = UserIban });
}

static async Task<McpClient> Connect(string url, string? taskScope)
{
    var options = new HttpClientTransportOptions { Endpoint = new Uri(url), Name = "scripted" };
    if (taskScope is not null)
        options.AdditionalHeaders = new Dictionary<string, string> { ["X-Task-Scope"] = taskScope };

    var client = await McpClient.CreateAsync(new HttpClientTransport(options));
    var tools = await client.ListToolsAsync();
    Console.WriteLine($"    connected with task scope '{taskScope ?? "-"}'; tools offered: "
                      + string.Join(", ", tools.Select(t => t.Name)));
    return client;
}

static async Task<string> Call(McpClient client, string tool, Dictionary<string, object?> args)
{
    var result = await client.CallToolAsync(tool, args);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    var status = result.IsError == true ? "DENIED " : "ok     ";
    var shown = text.ReplaceLineEndings(" ");
    Console.WriteLine($"  {status} {tool}({string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"))})");
    Console.WriteLine($"         -> {(shown.Length > 110 ? shown[..110] + "..." : shown)}");
    return text;
}

static string Excerpt(string text, string marker)
{
    var i = text.IndexOf(marker, StringComparison.Ordinal);
    if (i < 0) return "no hidden instruction found";
    var tail = text[i..].ReplaceLineEndings(" ");
    return tail.Length > 160 ? tail[..160] + "..." : tail;
}
