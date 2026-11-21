using FeeCalc.Api.Domain;
using FeeCalc.Api.Http;
using Microsoft.AspNetCore.Http;

namespace FeeCalc.Api.Tests;

public class FeeServiceTests
{
    private static readonly InMemoryRateTable Rates = new();
    private readonly FeeService _svc = new(Rates);

    [Fact]
    public async Task Calculate_known_rate_returns_Calculated()
    {
        var r = await _svc.CalculateAsync(new(AccountType.Brokerage, 10_000m, "gold"));
        var c = Assert.IsType<Calculated>(r.Value);
        Assert.Equal(50m, c.Fee);
    }

    [Fact]
    public async Task Calculate_tier_without_account_rate_returns_RateNotFound()
    {
        var r = await _svc.CalculateAsync(new(AccountType.Advisory, 100m, "silver"));
        Assert.IsType<RateNotFound>(r.Value);
    }

    [Fact]
    public async Task Rate_card_found_and_missing()
    {
        Assert.IsType<RateCard>((await _svc.GetRateCardAsync("gold")).Value);
        Assert.IsType<NotFound>((await _svc.GetRateCardAsync("nope")).Value);
    }
}

public class ValidatorTests
{
    private readonly FeeRequestValidator _v = new(new InMemoryRateTable());

    [Fact]
    public async Task Valid_request_has_no_errors() =>
        Assert.Empty(await _v.ValidateAsync(new(AccountType.Retirement, 1m, "bronze")));

    [Fact]
    public async Task Unknown_tier_and_bad_amount_are_reported()
    {
        var errors = await _v.ValidateAsync(new(AccountType.Retirement, 0m, "platinum"));
        Assert.Contains(errors, e => e.Field == "amount");
        Assert.Contains(errors, e => e.Field == "tier");
    }
}

public class MappingTests
{
    private static int Status(IResult r) => ((IStatusCodeHttpResult)r).StatusCode!.Value;

    [Fact] public void Calculated_is_200() => Assert.Equal(200, Status(((CalcResult)new Calculated(1, 1, "t", AccountType.Advisory)).ToHttp()));
    [Fact] public void RateNotFound_is_404() => Assert.Equal(404, Status(((CalcResult)new RateNotFound("t", AccountType.Advisory)).ToHttp()));
    [Fact] public void ValidationFailed_is_422() => Assert.Equal(422, Status(((CalcResult)new ValidationFailed([])).ToHttp()));
    [Fact] public void RateCard_is_200() => Assert.Equal(200, Status(((RateResult)new RateCard("t", new Dictionary<AccountType, decimal>())).ToHttp()));
    [Fact] public void NotFound_is_404() => Assert.Equal(404, Status(((RateResult)new NotFound("rate", "x")).ToHttp()));
}
