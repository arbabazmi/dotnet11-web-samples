using CommissionMcp.Server.Data;
using CommissionMcp.Server.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<CommissionStore>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Stateless: no server-to-client requests (sampling, elicitation) are needed, so each call stands alone.
        options.Stateless = true;
    })
    .WithTools<CommissionTools>();

var app = builder.Build();
app.MapMcp("/mcp");

app.Run();

public partial class Program;
