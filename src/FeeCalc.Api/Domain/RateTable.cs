namespace FeeCalc.Api.Domain;

public interface IRateTable
{
    Task<bool> TierExistsAsync(string tier, CancellationToken ct = default);
    Task<RateCard?> GetAsync(string tier, CancellationToken ct = default);
}

/// <summary>In-memory rate table with synthetic data. Methods are async to mimic a real store.</summary>
public sealed class InMemoryRateTable : IRateTable
{
    private static readonly Dictionary<string, RateCard> Cards = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bronze"] = new("bronze", new Dictionary<AccountType, decimal>
            { [AccountType.Brokerage] = 100m, [AccountType.Retirement] = 80m, [AccountType.Advisory] = 120m }),
        ["silver"] = new("silver", new Dictionary<AccountType, decimal>
            { [AccountType.Brokerage] = 75m, [AccountType.Retirement] = 60m }),   // no Advisory rate: drives RateNotFound
        ["gold"] = new("gold", new Dictionary<AccountType, decimal>
            { [AccountType.Brokerage] = 50m, [AccountType.Retirement] = 40m, [AccountType.Advisory] = 60m }),
    };

    public async Task<bool> TierExistsAsync(string tier, CancellationToken ct = default)
    {
        await Task.Yield();
        return Cards.ContainsKey(tier);
    }

    public async Task<RateCard?> GetAsync(string tier, CancellationToken ct = default)
    {
        await Task.Yield();
        return Cards.GetValueOrDefault(tier);
    }
}
