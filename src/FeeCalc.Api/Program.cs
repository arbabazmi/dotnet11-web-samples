using FeeCalc.Api.Domain;
using FeeCalc.Api.Http;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(o => o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_2);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<IRateTable, InMemoryRateTable>();
builder.Services.AddSingleton<IAsyncValidator<FeeRequest>, FeeRequestValidator>();
builder.Services.AddSingleton<FeeService>();

var app = builder.Build();
app.MapOpenApi();

var fees = app.MapGroup("/fees");

fees.MapPost("/calculate", async (FeeRequest request, FeeService svc, CancellationToken ct) =>
        (await svc.CalculateAsync(request, ct)).ToHttp())
    // Async validation runs before the handler; a failure short-circuits into the ValidationFailed case.
    .AddEndpointFilter(async (ctx, next) =>
    {
        var request = ctx.GetArgument<FeeRequest>(0);
        var validator = ctx.HttpContext.RequestServices.GetRequiredService<IAsyncValidator<FeeRequest>>();
        var errors = await validator.ValidateAsync(request, ctx.HttpContext.RequestAborted);
        return errors.Count > 0 ? ((CalcResult)new ValidationFailed(errors)).ToHttp(): await next(ctx);
    })
    .Produces<Calculated>(StatusCodes.Status200OK)
    .Produces<RateNotFound>(StatusCodes.Status404NotFound)
    .Produces<ValidationFailed>(StatusCodes.Status422UnprocessableEntity);

fees.MapGet("/rates/{tier}", async (string tier, FeeService svc, CancellationToken ct) =>
        (await svc.GetRateCardAsync(tier, ct)).ToHttp())
    .Produces<RateCard>(StatusCodes.Status200OK)
    .Produces<NotFound>(StatusCodes.Status404NotFound);

app.Run();

public partial class Program;
