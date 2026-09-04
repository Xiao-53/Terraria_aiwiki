[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [string] $UvPath = "uv"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$PythonVersion = "3.14.7"
$PythonUrl = "https://www.python.org/ftp/python/3.14.7/python-3.14.7-embeddable-amd64.zip"
$PythonSha256 = "76c3c0384ab3f822486f32450f3a4d20f5d65ad0ec32ee34290971aa0eb817e6"
$UvVersion = "0.12.9"
$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$AgentRoot = Join-Path $RepositoryRoot "agent"
$LockPath = Join-Path $AgentRoot "uv.lock"
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$TemporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("terraria-aiwiki-python-" + [Guid]::NewGuid().ToString("N"))
$RuntimeRoot = Join-Path $TemporaryRoot "python"
$ArchivePath = Join-Path $TemporaryRoot "python.zip"
$RequirementsPath = Join-Path $TemporaryRoot "requirements.txt"

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $FilePath"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

try {
    $actualUvVersion = (& $UvPath --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualUvVersion -notmatch "^uv $([Regex]::Escape($UvVersion))(\s|$)") {
        throw "Expected uv $UvVersion, got '$actualUvVersion'."
    }

    New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
    Invoke-WebRequest -Uri $PythonUrl -OutFile $ArchivePath -UseBasicParsing
    $actualPythonHash = Get-Sha256 $ArchivePath
    if ($actualPythonHash -ne $PythonSha256) {
        throw "CPython archive hash mismatch. Expected $PythonSha256, got $actualPythonHash."
    }
    Expand-Archive -Path $ArchivePath -DestinationPath $RuntimeRoot

    Push-Location $AgentRoot
    try {
        Invoke-Checked $UvPath export --quiet --frozen --no-dev --no-emit-project --format requirements.txt --output-file $RequirementsPath
    }
    finally {
        Pop-Location
    }

    $SitePackages = Join-Path $RuntimeRoot "Lib\site-packages"
    New-Item -ItemType Directory -Path $SitePackages -Force | Out-Null
    $EmbeddedPython = Join-Path $RuntimeRoot "python.exe"
    Invoke-Checked $UvPath pip install --quiet --target $SitePackages --python $EmbeddedPython --python-version 3.14 --python-platform x86_64-pc-windows-msvc --require-hashes --no-build --no-compile --requirements $RequirementsPath

    $PthPath = Join-Path $RuntimeRoot "python314._pth"
    @(
        "python314.zip"
        "."
        "Lib\site-packages"
        "..\src"
        "import site"
    ) | Set-Content -Path $PthPath -Encoding ascii

    Get-ChildItem -Path $SitePackages -Directory -Recurse -Force |
        Where-Object { $_.Name -in @("__pycache__", "test", "tests") } |
        Sort-Object FullName -Descending |
        Remove-Item -Recurse -Force
    Get-ChildItem -Path $SitePackages -File -Recurse -Force -Include *.pyc,*.pyo |
        Remove-Item -Force
    foreach ($directoryName in @("Scripts", "bin")) {
        $directory = Join-Path $SitePackages $directoryName
        if (Test-Path $directory) {
            Remove-Item -Path $directory -Recurse -Force
        }
    }

    $distributions = Get-ChildItem -Path $SitePackages -Directory -Filter "*.dist-info" |
        Sort-Object Name |
        ForEach-Object { $_.Name -replace '\.dist-info$', '' }
    $manifest = [ordered]@{
        schema = 1
        pythonVersion = $PythonVersion
        pythonUrl = $PythonUrl
        pythonSha256 = $PythonSha256
        uvVersion = $UvVersion
        lockSha256 = Get-Sha256 $LockPath
        distributions = @($distributions)
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText(
        (Join-Path $RuntimeRoot "runtime-manifest.json"),
        $manifestJson,
        [Text.UTF8Encoding]::new($false))

    if (Test-Path $OutputDirectory) {
        Remove-Item -Path $OutputDirectory -Recurse -Force
    }
    $outputParent = Split-Path -Parent $OutputDirectory
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
    Move-Item -Path $RuntimeRoot -Destination $OutputDirectory
}
finally {
    if (Test-Path $TemporaryRoot) {
        Remove-Item -Path $TemporaryRoot -Recurse -Force
    }
}
