# Aot.Bench: Project 3

One small Minimal API published four ways, to compare cold start, memory and image size.

| Variant | Target | Publish mode |
| --- | --- | --- |
| A | net10.0 | Framework-dependent / JIT |
| B | net10.0 | Native AOT |
| C | net11.0 | Framework-dependent / JIT |
| D | net11.0 | Native AOT |

Not a copy of `FeeCalc.Api`: that project uses C# 15 unions (they do not compile for net10.0) and
runtime-reflection OpenAPI generation. This one keeps the same fee-calculation endpoints
(`/fees/calculate`, `/fees/rates/{tier}`) with plain records, source-generated JSON and
`WebApplication.CreateSlimBuilder`, plus `/health` and `/diag` (runtime version, whether dynamic code is
supported, in-process time-to-ready, peak working set).

Source is identical for all variants. `<TargetFrameworks>` is `net10.0;net11.0`, and Native AOT is switched on
with `-p:Aot=true`. Each variant is built inside the matching official SDK image (`sdk:10.0` / `sdk:11.0`),
so a local .NET 10 SDK and the Windows C++ build tools are not needed.

## Run

Prerequisites: Docker running. From the repo root:

```powershell
./bench/bench.ps1                 # builds A-D, smoke-tests each, 30 cold runs, 100-request memory check
./bench/bench.ps1 -Runs 5 -Variants C,D -SkipBuild   # quick check
./bench/aws.ps1                   # Fargate (0.5 vCPU / 1 GB, x86_64); needs AWS credentials
./bench/aws.ps1 -Cleanup          # remove everything the script created
```

Metrics:
- **Ready**: process start to the server listening, measured inside the app.
- **First 200** (local only): wall clock from `docker run` to the first successful `/health`.
- **Peak mem**: peak working set after 100 requests. **Image**: `docker image inspect` size.
- Median and p95 (nearest rank) over the cold runs.

Results are written to `bench/results-*.csv` / `.md`. Record hardware, OS and Docker version next to them.
