<#
 Fargate run of the four Aot.Bench variants. Needs: Docker running, images built by bench.ps1 (aotbench:A..D),
 AWS credentials (aws configure / SSO). Every variant uses the same size: 0.5 vCPU, 1 GB, Linux x86_64.
 Usage:  ./bench/aws.ps1 [-Region us-east-1] [-Runs 30] [-Requests 100]
         ./bench/aws.ps1 -Cleanup      # deletes everything this script created
 Interleaves variants (A,B,C,D, A,B,C,D, ...) so AWS noise is spread evenly across them.
 Output: bench/results-aws.csv and bench/results-aws.md
 Cost: roughly 4 x Runs short-lived Fargate tasks (about a minute each).
#>
param(
  [string]$Region = $(if ($env:AWS_REGION) { $env:AWS_REGION } else { 'us-east-1' }),
  [int]$Runs = 30,
  [int]$Requests = 100,
  [switch]$Cleanup
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$root = Split-Path $PSScriptRoot -Parent; Set-Location $root
$Cluster = 'aotbench'; $Repo = 'aotbench'; $Sg = 'aotbench-sg'; $Role = 'aotbench-exec'

function Aws {
  $out = aws @args --region $Region --output json
  if ($LASTEXITCODE -ne 0) { throw "aws $($args -join ' ') failed" }
  if ($out) { $out | ConvertFrom-Json }
}

$account = (Aws sts get-caller-identity).Account
$registry = "$account.dkr.ecr.$Region.amazonaws.com"

if ($Cleanup) {
  foreach ($f in 'A','B','C','D') { try { Aws ecs deregister-task-definition --task-definition "aotbench-${f}:1" | Out-Null } catch {} }
  try { Aws ecs delete-cluster --cluster $Cluster | Out-Null } catch {}
  try { Aws ecr delete-repository --repository-name $Repo --force | Out-Null } catch {}
  try {
    $id = (Aws ec2 describe-security-groups --filters "Name=group-name,Values=$Sg").SecurityGroups[0].GroupId
    if ($id) { Aws ec2 delete-security-group --group-id $id | Out-Null }
  } catch {}
  try {
    Aws iam detach-role-policy --role-name $Role --policy-arn arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy | Out-Null
    Aws iam delete-role --role-name $Role | Out-Null
  } catch {}
  Write-Host 'Cleanup done.'
  return
}

# --- one-time setup ---------------------------------------------------------------------------
Write-Host "Account $account, region $Region"
try { Aws ecr describe-repositories --repository-names $Repo | Out-Null } catch { Aws ecr create-repository --repository-name $Repo | Out-Null }
aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin $registry | Out-Null
foreach ($f in 'A','B','C','D') {
  docker tag "aotbench:$f" "${registry}/${Repo}:$f"
  docker push "${registry}/${Repo}:$f" | Out-Null
}

try { $roleArn = (Aws iam get-role --role-name $Role).Role.Arn } catch {
  $trust = '{"Version":"2012-10-17","Statement":[{"Effect":"Allow","Principal":{"Service":"ecs-tasks.amazonaws.com"},"Action":"sts:AssumeRole"}]}'
  $roleArn = (Aws iam create-role --role-name $Role --assume-role-policy-document $trust).Role.Arn
  Aws iam attach-role-policy --role-name $Role --policy-arn arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy | Out-Null
  Start-Sleep -Seconds 10   # IAM propagation
}

$existing = (Aws ecs describe-clusters --clusters $Cluster).clusters | Where-Object { $_.status -eq 'ACTIVE' }
if (-not $existing) { Aws ecs create-cluster --cluster-name $Cluster | Out-Null }

$vpc = (Aws ec2 describe-vpcs --filters Name=isDefault,Values=true).Vpcs[0].VpcId
$subnets = ((Aws ec2 describe-subnets --filters "Name=vpc-id,Values=$vpc").Subnets | Select-Object -First 2).SubnetId -join ','
$myIp = (Invoke-RestMethod https://checkip.amazonaws.com).Trim()
$sgs = @((Aws ec2 describe-security-groups --filters "Name=group-name,Values=$Sg").SecurityGroups)
if ($sgs.Count) { $sgId = $sgs[0].GroupId } else {
  $sgId = (Aws ec2 create-security-group --group-name $Sg --description 'aotbench' --vpc-id $vpc).GroupId
  Aws ec2 authorize-security-group-ingress --group-id $sgId --protocol tcp --port 8080 --cidr "$myIp/32" | Out-Null
}

foreach ($f in 'A','B','C','D') {
  $td = @{
    family = "aotbench-$f"; networkMode = 'awsvpc'; requiresCompatibilities = @('FARGATE'); cpu = '512'; memory = '1024'
    executionRoleArn = $roleArn
    runtimePlatform = @{ cpuArchitecture = 'X86_64'; operatingSystemFamily = 'LINUX' }
    containerDefinitions = @(@{ name = 'app'; image = "${registry}/${Repo}:$f"; essential = $true; portMappings = @(@{ containerPort = 8080 }) })
  } | ConvertTo-Json -Depth 6 -Compress
  $file = Join-Path $env:TEMP "td-$f.json"
  [IO.File]::WriteAllText($file, $td)
  Aws ecs register-task-definition --cli-input-json "file://$file" | Out-Null
}

# --- helpers -------------------------------------------------------------------------------------
$http = [System.Net.Http.HttpClient]::new()
$http.Timeout = [TimeSpan]::FromSeconds(3)

function Start-FargateTask($f) {
  $net = "awsvpcConfiguration={subnets=[$subnets],securityGroups=[$sgId],assignPublicIp=ENABLED}"
  $arn = (Aws ecs run-task --cluster $Cluster --launch-type FARGATE --task-definition "aotbench-$f" --network-configuration $net).tasks[0].taskArn
  do {
    Start-Sleep -Seconds 3
    $t = (Aws ecs describe-tasks --cluster $Cluster --tasks $arn).tasks[0]
  } until ($t.lastStatus -in 'RUNNING', 'STOPPED')
  if ($t.lastStatus -eq 'STOPPED') { throw "Task stopped early: $($t.stoppedReason)" }
  $eni = ($t.attachments.details | Where-Object name -eq 'networkInterfaceId').value
  $ip = (Aws ec2 describe-network-interfaces --network-interface-ids $eni).NetworkInterfaces[0].Association.PublicIp
  [pscustomobject]@{ Arn = $arn; Url = "http://${ip}:8080" }
}

function Wait-Ok($url) {
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  while ([DateTime]::UtcNow -lt $deadline) {
    try { if ($http.GetAsync("$url/health").Result.IsSuccessStatusCode) { return } } catch {}
    Start-Sleep -Milliseconds 200
  }
  throw "No response from $url"
}

function Get-Percentile($v, [double]$p) {
  $s = @($v | Sort-Object)
  $s[[Math]::Max([Math]::Ceiling($p * $s.Count) - 1, 0)]
}

# --- measure -------------------------------------------------------------------------------------
$ready = @{ A = @(); B = @(); C = @(); D = @() }
$mem = @{}
for ($i = 1; $i -le $Runs; $i++) {
  foreach ($f in 'A','B','C','D') {
    Write-Host "run $i/$Runs variant $f"
    $t = Start-FargateTask $f
    try {
      Wait-Ok $t.Url
      $ready[$f] += ($http.GetStringAsync("$($t.Url)/diag").Result | ConvertFrom-Json).readyMs
      if ($i -eq 1) {   # memory after N requests, measured once per variant
        for ($r = 0; $r -lt $Requests; $r++) { [void]$http.GetStringAsync("$($t.Url)/fees/rates/gold").Result }
        $mem[$f] = ($http.GetStringAsync("$($t.Url)/diag").Result | ConvertFrom-Json).peakWorkingSetMb
      }
    } finally {
      Aws ecs stop-task --cluster $Cluster --task $t.Arn | Out-Null
    }
  }
}

$labels = @{ A = 'net10 JIT'; B = 'net10 AOT'; C = 'net11 JIT'; D = 'net11 AOT' }
$rows = 'A','B','C','D' | ForEach-Object {
  [pscustomobject]@{
    Variant = $_; Target = $labels[$_]
    ReadyMedianMs = [Math]::Round((Get-Percentile $ready[$_] 0.5), 1)
    ReadyP95Ms = [Math]::Round((Get-Percentile $ready[$_] 0.95), 1)
    PeakMemMb = [Math]::Round($mem[$_], 1); Runs = $Runs; Region = $Region; Size = '0.5 vCPU / 1 GB x86_64'
  }
}
$rows | Export-Csv -NoTypeInformation bench/results-aws.csv
$md = @("| Variant | Target | Ready median (ms) | Ready p95 (ms) | Peak mem after $Requests req (MB) |", '| --- | --- | ---: | ---: | ---: |')
foreach ($r in $rows) { $md += "| $($r.Variant) | $($r.Target) | $($r.ReadyMedianMs) | $($r.ReadyP95Ms) | $($r.PeakMemMb) |" }
$md | Set-Content bench/results-aws.md
$md
Write-Host 'Done. Run ./bench/aws.ps1 -Cleanup to remove all AWS resources.'
