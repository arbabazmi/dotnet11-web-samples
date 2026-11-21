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

Source is identical for all variants. the target is chosen with `-p:BenchTfm=net10.0` (default `net11.0`), and Native AOT is switched on
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

## Results: local (Docker, 30 cold runs per variant)

Run on 2026-11-21. Machine: Intel Core i7-1355U (12 logical CPUs), 16 GB, Windows 11 Pro, Docker 29.9.1 on WSL2
(kernel 6.6.114.1), Linux x64 containers. SDK images: `sdk:10.0` = 10.0.404 (runtime 10.0.15), `sdk:11.0` = 11.0.100 (runtime 11.0.0).

| Variant | Target | Ready median (ms) | Ready p95 (ms) | First 200 median (ms) | First 200 p95 (ms) | Peak mem after 100 req (MB) | Image (MB) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| A | net10 JIT | 548.9 | 824.5 | 1139.0 | 1574.8 | 64.8 | 234.4 |
| B | net10 AOT | 236.8 | 348.3 | 697.6 | 863.1 | 19.1 | 159.6 |
| C | net11 JIT | 534.9 | 614.2 | 1084.3 | 1270.2 | 66.9 | 246.2 |
| D | net11 AOT | 249.1 | 366.1 | 767.3 | 884.2 | 20.2 | 174.4 |

Raw data: `bench/results-local.csv`, `bench/results-local.md`.

- All four variants build and pass the same five smoke checks (health, rates 200 and 404, calculate 200 and 422).
- **Native AOT warnings: none** for either target (IL/trim/AOT warnings: 0). `JsonSerializerIsReflectionEnabledByDefault=false`
  plus a source-generated `JsonSerializerContext` and `CreateSlimBuilder` kept the app clean; see the publish logs by rebuilding
  with `--target build-aot`.
- **Build time** (first build with cached base images): AOT variants took about 70 to 77 s versus about 1 to 50 s for JIT
  (the JIT figures are skewed by layer cache and base-image pulls, so treat them as rough).

Caveats: variants were measured one after another (not interleaved), on a laptop with other processes running, so p95 values
carry noise. "Ready" is read from the process start time, which has clock-tick resolution on Linux. "First 200" includes
Docker's container start-up and my 5 ms polling interval, so compare variants with each other, not with other setups.
The net11 vs net10 differences here (for example JIT 535 vs 549 ms, AOT 249 vs 237 ms) are within run-to-run noise; the
JIT-vs-AOT gap is not.
