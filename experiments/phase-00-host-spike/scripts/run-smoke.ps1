[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$SmokeLog,
  [ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$experimentRoot = Split-Path -Parent $scriptRoot
$fullLog = [IO.Path]::GetFullPath($SmokeLog)
$artifactRoot = Join-Path $experimentRoot 'artifacts'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fullLog) | Out-Null
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
& $dotnet run --project (Join-Path $experimentRoot 'src\Phase00.HostSpike\Phase00.HostSpike.csproj') -c $Configuration --no-restore -- --smoke-log $fullLog
if ($LASTEXITCODE -ne 0) { throw "Host smoke process failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $fullLog)) { throw "Smoke log was not created: $fullLog" }
$result = Get-Content -Raw -LiteralPath $fullLog | ConvertFrom-Json
if (-not $result.success) { throw "Host smoke reported failure: $($result.message)" }
Write-Host "Host smoke passed: $fullLog"
