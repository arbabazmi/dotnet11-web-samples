using System.Text.Json.Serialization;

namespace FeeCalc.Api.Domain;

public enum AccountType { Brokerage, Retirement, Advisory }

public record FeeRequest(AccountType AccountType, decimal Amount, string Tier);

// Each union case carries a "kind" discriminator so clients can tell cases apart in JSON.
public record Calculated(decimal Fee, decimal RateBps, string Tier, AccountType AccountType)
{
    [JsonPropertyName("kind")] public string Kind => "calculated";
}

public record RateNotFound(string Tier, AccountType AccountType)
{
    [JsonPropertyName("kind")] public string Kind => "rate_not_found";
}

public record ValidationFailed(IReadOnlyList<ValidationError> Errors)
{
    [JsonPropertyName("kind")] public string Kind => "validation_failed";
}

public record ValidationError(string Field, string Message);

public record RateCard(string Tier, IReadOnlyDictionary<AccountType, decimal> RatesBps)
{
    [JsonPropertyName("kind")] public string Kind => "rate_card";
}

public record NotFound(string Resource, string Id)
{
    [JsonPropertyName("kind")] public string Kind => "not_found";
}

// C# 15 unions: closed sets of cases, enforced by exhaustive switches.
public union CalcResult(Calculated, RateNotFound, ValidationFailed);
public union RateResult(RateCard, NotFound);
