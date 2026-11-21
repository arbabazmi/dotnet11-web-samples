using System.Diagnostics;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, BenchJsonContext.Default));

var app = builder.Build();

// Rate table: synthetic, same shape as FeeCalc.Api (Project 1) but without C# 15 unions or OpenAPI.
var rates = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
{
    ["bronze"] = new() { ["Brokerage"] = 100m, ["Retirement"] = 80m, ["Advisory"] = 120m },
    ["silver"] = new() { ["Brokerage"] = 75m, ["Retirement"] = 60m },
    ["gold"]   = new() { ["Brokerage"] = 50m, ["Retirement"] = 40m, ["Advisory"] = 60m },
};

// Process start -> server listening, measured inside the app.
double readyMs = -1;
app.Lifetime.ApplicationStarted.Register(() =>
    readyMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/fees/rates/{tier}", (string tier) =>
    rates.TryGetValue(tier, out var card)
        ? Results.Ok(new RateCard(tier.ToLowerInvariant(), card))
        : Results.NotFound(new ErrorBody("not_found", $"Tier '{tier}' not found.")));

app.MapPost("/fees/calculate", (FeeRequest r) =>
{
    if (r.Amount <= 0) return Results.UnprocessableEntity(new ErrorBody("validation_failed", "Amount must be greater than zero."));
    if (!rates.TryGetValue(r.Tier, out var card)) return Results.UnprocessableEntity(new ErrorBody("validation_failed", $"Tier '{r.Tier}' does not exist."));
    if (!card.TryGetValue(r.AccountType, out var bps)) return Results.NotFound(new ErrorBody("rate_not_found", "No rate for that account type."));
    return Results.Ok(new FeeResult(Math.Round(r.Amount * bps / 10_000m, 2, MidpointRounding.AwayFromZero), bps));
});

// Diagnostics read by the benchmark script.
app.MapGet("/diag", () =>
{
    var p = Process.GetCurrentProcess();
    return Results.Ok(new Diag(
        Environment.Version.ToString(),
        System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
        readyMs,
        p.PeakWorkingSet64 / (1024.0 * 1024.0)));
});

app.Run();

public record FeeRequest(string AccountType, decimal Amount, string Tier);
public record FeeResult(decimal Fee, decimal RateBps);
public record RateCard(string Tier, Dictionary<string, decimal> RatesBps);
public record ErrorBody(string Kind, string Message);
public record Diag(string Runtime, bool DynamicCode, double ReadyMs, double PeakWorkingSetMb);

[JsonSerializable(typeof(FeeRequest))]
[JsonSerializable(typeof(FeeResult))]
[JsonSerializable(typeof(RateCard))]
[JsonSerializable(typeof(ErrorBody))]
[JsonSerializable(typeof(Diag))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class BenchJsonContext : JsonSerializerContext;
