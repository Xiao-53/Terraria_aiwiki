[CmdletBinding()]
param(
  [Parameter(Mandatory = $false)][string]$Package,
  [Parameter(Mandatory = $false)][string]$Database,
  [Parameter(Mandatory = $false)][string]$TlsUrl
)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$experimentRoot = Split-Path -Parent $scriptRoot
$artifactRoot = Join-Path $experimentRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$runner = Join-Path $experimentRoot 'src\Phase00.ProbeRunner\Phase00.ProbeRunner.csproj'
& $dotnet run --project $runner -c Release --no-restore -- fixture --output (Join-Path $artifactRoot 'fixture.json')
if ($LASTEXITCODE -ne 0) { throw "fixture evidence failed with exit code $LASTEXITCODE" }
if ($Package) {
  # A package is never implicit. The oracle streams it through a disposable
  # staging directory and the JSON contains metadata/hash evidence only.
  & $dotnet run --project $runner -c Release --no-restore -- package --package ([IO.Path]::GetFullPath($Package)) --output (Join-Path $artifactRoot 'package.json')
  if ($LASTEXITCODE -ne 0) { throw "package evidence failed with exit code $LASTEXITCODE" }
}
if ($Database) {
  & $dotnet run --project $runner -c Release --no-restore -- database --database ([IO.Path]::GetFullPath($Database)) --output (Join-Path $artifactRoot 'database.json')
  if ($LASTEXITCODE -ne 0) { throw "database evidence failed with exit code $LASTEXITCODE" }
}
if ($TlsUrl) {
  $tlsProbe = Join-Path $experimentRoot 'src\Phase00.TlsProbe\Phase00.TlsProbe.csproj'
  & $dotnet run --project $tlsProbe -c Release --no-restore -- --url $TlsUrl --output (Join-Path $artifactRoot 'tls.json')
  if ($LASTEXITCODE -ne 0) { throw "TLS evidence failed with exit code $LASTEXITCODE" }
}
Write-Host "Evidence outputs are under $artifactRoot"
