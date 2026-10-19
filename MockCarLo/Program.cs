// Mock of the CarLo MCP server (path B in the architecture model, flows K7/K8).
// It exposes three tools over the MCP Streamable HTTP transport and holds a
// handful of in-memory records. No validation happens here on purpose: the
// mock stands for the vendor's server, which executes every call that arrives
// with a valid token (threat T7). The validation layer sits in front of it.

using MockCarLo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CarLoStore>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "mock-carlo", Version = "0.1" };
    })
    .WithHttpTransport()
    .WithTools<CarLoTools>();

var app = builder.Build();
app.MapMcp("/mcp");

var url = args.Length > 0 ? args[0] : "http://localhost:5101";
Console.WriteLine($"mock-carlo listening on {url}/mcp");
app.Run(url);
