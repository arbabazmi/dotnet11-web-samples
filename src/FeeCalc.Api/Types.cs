public record Calculated(decimal Fee);
public record RateNotFound(string Tier);
public record ValidationFailed(string[] Errors);
public union CalcResult(Calculated, RateNotFound, ValidationFailed);
