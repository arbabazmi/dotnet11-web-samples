var b = WebApplication.CreateBuilder(args);
b.Services.AddOpenApi(o => o.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_2);
var app = b.Build();
app.MapOpenApi();
app.MapGet("/u", CalcResult () => new RateNotFound("Z"));
app.MapGet("/c", () => new Calculated(2m));
app.Run();
