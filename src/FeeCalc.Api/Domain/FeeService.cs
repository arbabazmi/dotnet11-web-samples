namespace FeeCalc.Api.Domain;

public sealed class FeeService(IRateTable rates)
{
    public async Task<CalcResult> CalculateAsync(FeeRequest request, CancellationToken ct = default)
    {
        var card = await rates.GetAsync(request.Tier, ct);
        if (card is null || !card.RatesBps.TryGetValue(request.AccountType, out var bps))
            return new RateNotFound(request.Tier, request.AccountType);

        var fee = Math.Round(request.Amount * bps / 10_000m, 2, MidpointRounding.AwayFromZero);
        return new Calculated(fee, bps, card.Tier, request.AccountType);
    }

    public async Task<RateResult> GetRateCardAsync(string tier, CancellationToken ct = default)
    {
        var card = await rates.GetAsync(tier, ct);
        return card is null ? new NotFound("rate", tier) : card;
    }
}
