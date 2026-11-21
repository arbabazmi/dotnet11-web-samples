using FeeCalc.Api.Domain;

namespace FeeCalc.Api.Http;

/// <summary>
/// Maps union cases to HTTP results. Each switch is exhaustive: with CS8509 treated as an error,
/// adding a case to a union without handling it here fails the build.
/// </summary>
public static class UnionHttpMapping
{
    public static IResult ToHttp(this CalcResult result) => result switch
    {
        Calculated c        => Results.Ok(c),
        RateNotFound n      => Results.NotFound(n),
        ValidationFailed v  => Results.UnprocessableEntity(v),
    };

    public static IResult ToHttp(this RateResult result) => result switch
    {
        RateCard c => Results.Ok(c),
        NotFound n => Results.NotFound(n),
    };
}
