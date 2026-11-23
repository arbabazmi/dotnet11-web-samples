# FeeCalc.Api: Project 1

Minimal API where endpoints return **C# 15 union types**, mapped to HTTP with exhaustive `switch` expressions. All data is synthetic.

Versions: .NET SDK 11.0.100, `net11.0`, `Microsoft.AspNetCore.OpenApi` 11.0.0.

## Run / test

```powershell
dotnet run --project src/FeeCalc.Api
dotnet test
```

| Method | Route | Returns | Status |
| --- | --- | --- | --- |
| POST | /fees/calculate | `Calculated \| RateNotFound \| ValidationFailed` | 200, 404, 422 |
| GET | /fees/rates/{tier} | `RateCard \| NotFound` | 200, 404 |
| GET | /openapi/v1.json | OpenAPI 3.2 document | 200 |

Tiers: `bronze`, `silver`, `gold`. `silver` has no Advisory rate, which triggers `RateNotFound`.

```powershell
curl -H "content-type: application/json" localhost:5000/fees/calculate -d '{"accountType":"Brokerage","amount":10000,"tier":"gold"}'
```

## Findings

- **Unions compile** on SDK 11.0.100 (`public union CalcResult(Calculated, RateNotFound, ValidationFailed);`).
- **Exhaustiveness is a warning by default** (CS8509). The project sets `<WarningsAsErrors>CS8509</WarningsAsErrors>` so a missing case fails the build; see `docs/articles/compiler-diagnostic.txt`.
- **JSON has no discriminator.** A union serializes as the bare case, so each case carries an explicit `kind` property.
- **OpenAPI 3.2** is emitted (`"openapi": "3.2.0"`). A handler that returns a union type directly produced a `CalcResult` schema with `anyOf` over the cases, but with only a 200 response. Because handlers here return `IResult` (after `ToHttp()`), the cases are declared with `.Produces<T>(status)`, and the union types themselves do not appear in `components/schemas`.
- `decimal` is rendered as `["number","string"]` with `format: double`.
