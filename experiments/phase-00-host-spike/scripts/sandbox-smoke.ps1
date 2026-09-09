[CmdletBinding()]
param(
  [ValidateRange(30, 900)][int]$TimeoutSeconds = 120,
  [ValidateSet('Available', 'Missing')][string]$ExpectedWebView2State = 'Available',
  [string]$WebView2RuntimeFolder
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$experimentRoot = Split-Path -Parent $scriptRoot
$artifactRoot = Join-Path $experimentRoot 'artifacts\sandbox-smoke'
$sharedRoot = Join-Path $artifactRoot 'shared'
$publishRoot = Join-Path $sharedRoot 'publish'
$evidenceRoot = Join-Path $sharedRoot 'evidence'
$resultPath = Join-Path $evidenceRoot 'sandbox-result.json'
$configPath = Join-Path $artifactRoot 'phase00.wsb'
$sandboxExe = Join-Path $env:WINDIR 'System32\WindowsSandbox.exe'

if (-not (Test-Path -LiteralPath $sandboxExe)) {
  throw 'Windows Sandbox executable is unavailable. Enable Windows Sandbox or use an equivalent clean VM/test machine.'
}

if ($ExpectedWebView2State -eq 'Missing' -and -not [string]::IsNullOrWhiteSpace($WebView2RuntimeFolder)) {
  throw '-WebView2RuntimeFolder cannot be used when -ExpectedWebView2State is Missing.'
}

$runtimeMapping = ''
$runtimeSetup = ''
if (-not [string]::IsNullOrWhiteSpace($WebView2RuntimeFolder)) {
  $runtimeFull = [IO.Path]::GetFullPath($WebView2RuntimeFolder)
  if (-not (Test-Path -LiteralPath (Join-Path $runtimeFull 'msedgewebview2.exe'))) {
    throw "WebView2 runtime folder does not contain msedgewebview2.exe: $runtimeFull"
  }
  $escapedRuntime = [Security.SecurityElement]::Escape($runtimeFull)
  $runtimeMapping = @"
    <MappedFolder>
      <HostFolder>$escapedRuntime</HostFolder>
      <SandboxFolder>C:\WebView2Runtime</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
"@
  $runtimeSetup = '$env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER = ''C:\WebView2Runtime'''
}

if (Test-Path -LiteralPath $artifactRoot) {
  Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishRoot, $evidenceRoot | Out-Null

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = 'C:\Program Files\dotnet\dotnet.exe' }
$project = Join-Path $experimentRoot 'src\Phase00.HostSpike\Phase00.HostSpike.csproj'
& $dotnet publish $project -c Release -r win-x64 --self-contained:true -o $publishRoot --nologo
if ($LASTEXITCODE -ne 0) { throw "self-contained sandbox publish failed with exit code $LASTEXITCODE" }

$entryScript = @'
$ErrorActionPreference = 'Stop'
__WEBVIEW2_RUNTIME_SETUP__
$sharedRoot = 'C:\Phase00'
$evidenceRoot = Join-Path $sharedRoot 'evidence'
$smokePath = Join-Path $evidenceRoot 'host-smoke.json'
$resultPath = Join-Path $evidenceRoot 'sandbox-result.json'
$environmentPath = Join-Path $evidenceRoot 'sandbox-environment.json'
$result = [ordered]@{
  success = $false
  completedUtc = [DateTimeOffset]::UtcNow
  expectedWebView2State = '__EXPECTED_WEBVIEW2_STATE__'
  actualWebView2Available = $null
  expectationMet = $false
  hostExitCode = $null
  smokeSuccess = $false
  error = $null
}

try {
  $runtimeKey = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
  $runtime = if (Test-Path $runtimeKey) { Get-ItemProperty $runtimeKey } else { $null }
  $environment = [ordered]@{
    completedUtc = [DateTimeOffset]::UtcNow
    osVersion = [Environment]::OSVersion.VersionString
    osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    is64BitOperatingSystem = [Environment]::Is64BitOperatingSystem
    dotnetCommand = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
    webView2Version = if ($runtime) { $runtime.pv } else { $null }
  }
  $environment | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $environmentPath -Encoding UTF8

  $exe = Join-Path $sharedRoot 'publish\Phase00.HostSpike.exe'
  if (-not (Test-Path -LiteralPath $exe)) { throw "Published host is missing: $exe" }
  $process = Start-Process -FilePath $exe -ArgumentList @('--smoke-log', $smokePath) -Wait -PassThru
  $result.hostExitCode = $process.ExitCode
  if (-not (Test-Path -LiteralPath $smokePath)) { throw 'Host smoke JSON was not created.' }
  $smoke = Get-Content -Raw -LiteralPath $smokePath | ConvertFrom-Json
  $result.smokeSuccess = [bool]$smoke.success
  $result.actualWebView2Available = [bool]$smoke.webView2.available
  if ($result.expectedWebView2State -eq 'Available') {
    $result.expectationMet = [bool]$smoke.webView2.available -and [bool]$smoke.success
  }
  else {
    $result.expectationMet = -not [bool]$smoke.webView2.available -and -not [bool]$smoke.success `
      -and ([string]$smoke.webView2.error).StartsWith('Microsoft Edge WebView2 Runtime was not found', [StringComparison]::Ordinal)
  }
  $result.success = $process.ExitCode -eq 0 -and $result.expectationMet
}
catch {
  $result.error = [ordered]@{ type = $_.Exception.GetType().Name; message = $_.Exception.Message }
}
finally {
  $result.completedUtc = [DateTimeOffset]::UtcNow
  $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
  Start-Process -FilePath "$env:WINDIR\System32\shutdown.exe" -ArgumentList @('/s', '/t', '0') -WindowStyle Hidden
}
'@
$entryScript = $entryScript.Replace('__WEBVIEW2_RUNTIME_SETUP__', $runtimeSetup)
$entryScript = $entryScript.Replace('__EXPECTED_WEBVIEW2_STATE__', $ExpectedWebView2State)
$entryPath = Join-Path $sharedRoot 'sandbox-entry.ps1'
Set-Content -LiteralPath $entryPath -Value $entryScript -Encoding UTF8

$escapedHost = [Security.SecurityElement]::Escape($sharedRoot)
$config = @"
<Configuration>
  <VGpu>Disable</VGpu>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$escapedHost</HostFolder>
      <SandboxFolder>C:\Phase00</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
$runtimeMapping
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\Phase00\sandbox-entry.ps1</Command>
  </LogonCommand>
</Configuration>
"@
Set-Content -LiteralPath $configPath -Value $config -Encoding UTF8

$sandbox = Start-Process -FilePath $sandboxExe -ArgumentList @($configPath) -PassThru
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
while ([DateTimeOffset]::UtcNow -lt $deadline -and -not (Test-Path -LiteralPath $resultPath)) {
  if ($sandbox.HasExited) { break }
  Start-Sleep -Seconds 2
}

if (-not (Test-Path -LiteralPath $resultPath)) {
  # Mapped-folder writes can remain invisible to the host until the disposable
  # VM closes.  Stop only the Sandbox instance started above, then give the
  # mapping a short flush window before deciding that evidence is absent.
  if (-not $sandbox.HasExited) {
    Stop-Process -Id $sandbox.Id -Force -ErrorAction SilentlyContinue
    $sandbox.WaitForExit(15000)
  }
  $flushDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
  while ([DateTimeOffset]::UtcNow -lt $flushDeadline -and -not (Test-Path -LiteralPath $resultPath)) {
    Start-Sleep -Milliseconds 500
  }
}

if (-not (Test-Path -LiteralPath $resultPath)) {
  throw "Windows Sandbox did not produce evidence within $TimeoutSeconds seconds plus the mapping flush window."
}

$result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
if (-not $result.success) {
  $detail = if ($result.error) { $result.error.message } else {
    "expected WebView2=$($result.expectedWebView2State), actual available=$($result.actualWebView2Available), smoke success=$($result.smokeSuccess)"
  }
  throw "Windows Sandbox smoke failed: $detail"
}

Write-Host "Windows Sandbox smoke passed: $resultPath"
