<#
 Cold-start benchmark for the four Aot.Bench variants (run from the repo root, Docker running):
   A net10 JIT   B net10 AOT   C net11 JIT   D net11 AOT
 Usage: ./bench/bench.ps1 [-Runs 30] [-Requests 100] [-Variants A,B,C,D] [-SkipBuild]
 Output: bench/results-local.csv and bench/results-local.md
#>
param(
  [int]$Runs = 30,
  [int]$Requests = 100,
  [string[]]$Variants = @('A','B','C','D'),
  [switch]$SkipBuild,
  [int]$Port = 18080
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$defs = @{
  A = @{ Dotnet='10.0'; Tfm='net10.0'; Target='jit'; Label='net10 JIT' }
  B = @{ Dotnet='10.0'; Tfm='net10.0'; Target='aot'; Label='net10 AOT' }
  C = @{ Dotnet='11.0'; Tfm='net11.0'; Target='jit'; Label='net11 JIT' }
  D = @{ Dotnet='11.0'; Tfm='net11.0'; Target='aot'; Label='net11 AOT' }
}
$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(2)
$base = "http://localhost:$Port"

function Get-Percentile($values, [double]$p) {
  $s = @($values | Sort-Object)
  $i = [Math]::Max([Math]::Ceiling($p * $s.Count) - 1, 0)
  return $s[$i]
}
function Start-Container($tag) {
  docker run -d --rm -p "${Port}:8080" --name aotbench-run "aotbench:$tag" | Out-Null
}
function Stop-Container { docker rm -f aotbench-run 2>$null | Out-Null }
function Wait-Health {
  while ($true) {
    try { if ($http.GetAsync("$base/health").Result.IsSuccessStatusCode) { return } } catch { }
    Start-Sleep -Milliseconds 5
  }
}
function Invoke-Smoke {
  $json = [System.Net.Http.StringContent]::new('{"accountType":"Brokerage","amount":10000,"tier":"gold"}', [Text.Encoding]::UTF8, 'application/json')
  $bad  = [System.Net.Http.StringContent]::new('{"accountType":"Brokerage","amount":-1,"tier":"gold"}', [Text.Encoding]::UTF8, 'application/json')
  $checks = @(
    @{ n='health';        c=$http.GetAsync("$base/health").Result.StatusCode.value__;           e=200 },
    @{ n='rates gold';    c=$http.GetAsync("$base/fees/rates/gold").Result.StatusCode.value__;  e=200 },
    @{ n='rates missing'; c=$http.GetAsync("$base/fees/rates/nope").Result.StatusCode.value__;   e=404 },
    @{ n='calc ok';       c=$http.PostAsync("$base/fees/calculate",$json).Result.StatusCode.value__; e=200 },
    @{ n='calc invalid';  c=$http.PostAsync("$base/fees/calculate",$bad).Result.StatusCode.value__;  e=422 }
  )
  foreach ($k in $checks) { if ($k.c -ne $k.e) { throw "Smoke test '$($k.n)': expected $($k.e), got $($k.c)" } }
}

$rows = @()
foreach ($v in $Variants) {
  $d = $defs[$v]; $tag = $v
  Write-Host "=== Variant $v ($($d.Label)) ==="
  if (-not $SkipBuild) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    docker build -f bench/Dockerfile --target $d.Target -t "aotbench:$tag" `
      --build-arg DOTNET=$($d.Dotnet) --build-arg TFM=$($d.Tfm) . | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker build failed for $v" }
    $buildSec = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
  } else { $buildSec = $null }
  $sizeMb = [Math]::Round([double](docker image inspect "aotbench:$tag" --format '{{.Size}}') / 1MB, 1)

  Stop-Container
  Start-Container $tag; Wait-Health; Invoke-Smoke; Stop-Container   # smoke tests
  Write-Host "  smoke tests passed"

  $wall = @(); $ready = @()
  for ($i = 1; $i -le $Runs; $i++) {
    Stop-Container
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Start-Container $tag; Wait-Health
    $wall += $sw.Elapsed.TotalMilliseconds
    $diag = $http.GetStringAsync("$base/diag").Result | ConvertFrom-Json
    $ready += $diag.readyMs
  }
  Stop-Container

  # Peak memory after N requests on a fresh container.
  Start-Container $tag; Wait-Health
  for ($i = 0; $i -lt $Requests; $i++) { [void]$http.GetStringAsync("$base/fees/rates/gold").Result }
  $diag = $http.GetStringAsync("$base/diag").Result | ConvertFrom-Json
  Stop-Container

  $rows += [pscustomobject]@{
    Variant=$v; Target=$d.Label; Runtime=$diag.runtime; DynamicCode=$diag.dynamicCode
    ReadyMedianMs=[Math]::Round((Get-Percentile $ready 0.5),1); ReadyP95Ms=[Math]::Round((Get-Percentile $ready 0.95),1)
    FirstOkMedianMs=[Math]::Round((Get-Percentile $wall 0.5),1); FirstOkP95Ms=[Math]::Round((Get-Percentile $wall 0.95),1)
    PeakMemMb=[Math]::Round($diag.peakWorkingSetMb,1); ImageMb=$sizeMb; BuildSec=$buildSec; Runs=$Runs
  }
}

$rows | Export-Csv -NoTypeInformation -Path bench/results-local.csv
$md = @('| Variant | Target | Ready median (ms) | Ready p95 (ms) | First 200 median (ms) | First 200 p95 (ms) | Peak mem after {0} req (MB) | Image (MB) | Build (s) |' -f $Requests,
        '| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
foreach ($r in $rows) {
  $md += "| $($r.Variant) | $($r.Target) | $($r.ReadyMedianMs) | $($r.ReadyP95Ms) | $($r.FirstOkMedianMs) | $($r.FirstOkP95Ms) | $($r.PeakMemMb) | $($r.ImageMb) | $($r.BuildSec) |"
}
$md | Set-Content bench/results-local.md
$md
