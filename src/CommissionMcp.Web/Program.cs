using CommissionMcp.Web.Components;
using CommissionMcp.Web.Services;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MCP server connection (Mcp:Endpoint) and the chat client (Chat:* settings; the API key comes from user secrets or env vars).
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));
builder.Services.AddSingleton<McpToolProvider>();
builder.Services.AddSingleton<IChatClient>(sp => ChatClientFactory.CreateWithTools(sp.GetRequiredService<IConfiguration>()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
