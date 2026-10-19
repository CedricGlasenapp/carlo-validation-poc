// Scripted MCP client that replays scenario 1. It stands in for the
// customer's AI client on path B (K7). No language model is involved: the
// call sequence is the one an injected model would produce after reading
// DOC-4711, so that the demonstration is deterministic. The validation layer
// cannot tell a scripted call from a model's call, which is the point.

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var proxyUrl = args.Length > 0 ? args[0] : "http://localhost:5100/mcp";

Console.WriteLine("=== Run A: task 'summarise-document' (the attack) ===");
await using (var client = await Connect(proxyUrl))
{
    await Call(client, "declare_task", new() { ["task"] = "summarise-document" });

    // 1. The user asks for a summary of the carrier's confirmation.
    var doc = await Call(client, "ReadDocument", new() { ["documentId"] = "DOC-4711" });

    // 2. The document contains a hidden instruction. An injected model would
    //    now propose the write it asks for. We issue exactly that call.
    Console.WriteLine("    (document contains: " + Excerpt(doc, "NOTE TO THE AI ASSISTANT") + ")");
    await Call(client, "UpdateCustomerBankAccount",
        new() { ["customerId"] = "K-1001", ["iban"] = "DE75 5121 0800 1245 1261 99" });

    // 3. A legitimate read within the task still works.
    await Call(client, "GetTransportOrder", new() { ["orderId"] = "TO-2026-0815" });
}

Console.WriteLine();
Console.WriteLine("=== Run B: task 'maintain-customer' (the same write, in scope) ===");
await using (var client = await Connect(proxyUrl))
{
    await Call(client, "declare_task", new() { ["task"] = "maintain-customer" });
    await Call(client, "UpdateCustomerBankAccount",
        new() { ["customerId"] = "K-1002", ["iban"] = "DE27 1007 7777 0209 2995 00" });
    await Call(client, "ReadDocument", new() { ["documentId"] = "DOC-4711" });
}

Console.WriteLine();
Console.WriteLine("=== Run C: no task declared ===");
await using (var client = await Connect(proxyUrl))
{
    await Call(client, "GetTransportOrder", new() { ["orderId"] = "TO-2026-0815" });
}

static async Task<McpClient> Connect(string url)
{
    var client = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri(url), Name = "scripted" }));
    var tools = await client.ListToolsAsync();
    Console.WriteLine("    connected; tools offered: " + string.Join(", ", tools.Select(t => t.Name)));
    return client;
}

static async Task<string> Call(McpClient client, string tool, Dictionary<string, object?> args)
{
    var result = await client.CallToolAsync(tool, args);
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
    var status = result.IsError == true ? "DENIED " : "ok     ";
    var shown = text.ReplaceLineEndings(" ");
    Console.WriteLine($"  {status} {tool}({string.Join(", ", args.Select(a => $"{a.Key}={a.Value}"))})");
    Console.WriteLine($"         -> {(shown.Length > 100 ? shown[..100] + "..." : shown)}");
    return text;
}

static string Excerpt(string text, string marker)
{
    var i = text.IndexOf(marker, StringComparison.Ordinal);
    if (i < 0) return "no hidden instruction found";
    var tail = text[i..].ReplaceLineEndings(" ");
    return tail.Length > 160 ? tail[..160] + "..." : tail;
}
