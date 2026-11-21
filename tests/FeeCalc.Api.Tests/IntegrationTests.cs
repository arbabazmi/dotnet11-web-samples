using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FeeCalc.Api.Tests;

public class EndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.CreateClient();

    [Fact]
    public async Task Calculate_returns_200_with_calculated_case()
    {
        var res = await _http.PostAsJsonAsync("/fees/calculate", new { accountType = "Brokerage", amount = 10000, tier = "gold" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("calculated", body.GetProperty("kind").GetString());
        Assert.Equal(50m, body.GetProperty("fee").GetDecimal());
    }

    [Fact]
    public async Task Calculate_returns_404_when_no_rate_for_account_type()
    {
        var res = await _http.PostAsJsonAsync("/fees/calculate", new { accountType = "Advisory", amount = 100, tier = "silver" });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("rate_not_found", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Calculate_returns_422_with_structured_errors_for_invalid_input()
    {
        var res = await _http.PostAsJsonAsync("/fees/calculate", new { accountType = "Brokerage", amount = -1, tier = "platinum" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", body.GetProperty("kind").GetString());
        Assert.Equal(2, body.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task Rates_returns_200_then_404()
    {
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/fees/rates/gold")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/fees/rates/nope")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_declares_3_2_and_lists_every_case()
    {
        var doc = await _http.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.StartsWith("3.2", doc.GetProperty("openapi").GetString());
        var calc = doc.GetProperty("paths").GetProperty("/fees/calculate").GetProperty("post").GetProperty("responses");
        foreach (var code in new[] { "200", "404", "422" }) Assert.True(calc.TryGetProperty(code, out _), code);
        var rates = doc.GetProperty("paths").GetProperty("/fees/rates/{tier}").GetProperty("get").GetProperty("responses");
        foreach (var code in new[] { "200", "404" }) Assert.True(rates.TryGetProperty(code, out _), code);
    }
}
