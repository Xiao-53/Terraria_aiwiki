[CmdletBinding()]
param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$experimentRoot = Split-Path -Parent $scriptRoot
$publishRoot = Join-Path $experimentRoot 'artifacts\publish'
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$project = Join-Path $experimentRoot 'src\Phase00.HostSpike\Phase00.HostSpike.csproj'
& $dotnet publish $project -c Release -r win-x64 --self-contained:$false -o (Join-Path $publishRoot 'framework-dependent') --nologo
if ($LASTEXITCODE -ne 0) { throw "framework-dependent publish failed with exit code $LASTEXITCODE" }
& $dotnet publish $project -c Release -r win-x64 --self-contained:true -o (Join-Path $publishRoot 'self-contained-win-x64') --nologo
if ($LASTEXITCODE -ne 0) { throw "self-contained publish failed with exit code $LASTEXITCODE" }
Write-Host "Publish outputs: $publishRoot"
