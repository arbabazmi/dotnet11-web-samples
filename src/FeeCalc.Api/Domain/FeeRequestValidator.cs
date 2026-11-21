namespace FeeCalc.Api.Domain;

public interface IAsyncValidator<T>
{
    Task<IReadOnlyList<ValidationError>> ValidateAsync(T value, CancellationToken ct = default);
}

public sealed class FeeRequestValidator(IRateTable rates) : IAsyncValidator<FeeRequest>
{
    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(FeeRequest r, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();
        if (!Enum.IsDefined(r.AccountType))
            errors.Add(new("accountType", "Unknown account type."));
        if (r.Amount <= 0)
            errors.Add(new("amount", "Amount must be greater than zero."));
        if (string.IsNullOrWhiteSpace(r.Tier))
            errors.Add(new("tier", "Tier is required."));
        else if (!await rates.TierExistsAsync(r.Tier, ct))   // the async check
            errors.Add(new("tier", $"Tier '{r.Tier}' does not exist."));
        return errors;
    }
}
