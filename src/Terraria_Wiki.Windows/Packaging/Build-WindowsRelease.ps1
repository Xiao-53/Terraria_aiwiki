[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $RepositoryUrl,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [string] $DotNetPath = "dotnet",
    [string] $VpkPath = "vpk",
    [string] $UvPath = "uv",
    [string] $BundledPythonSource,
    [string] $PreviousReleaseDirectory,
    [string] $ProjectLicenseFile,
    [switch] $AllowDirtySource,
    [switch] $AllowMissingProjectLicense
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$PackId = "TerrariaAIWiki.Windows"
$Channel = "win-x64-stable"
$RuntimeIdentifier = "win-x64"
$VpkVersion = "1.2.0"
$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$ProjectPath = Join-Path $RepositoryRoot "src\Terraria_Wiki.Windows\Terraria_Wiki.Windows.csproj"
$PublishProfile = "win-x64"
$ArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot "artifacts"))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$TemporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("terraria-aiwiki-release-" + [Guid]::NewGuid().ToString("N"))
$PublishDirectory = Join-Path $TemporaryRoot "publish"
$PackageDirectory = Join-Path $TemporaryRoot "packages"
$NoticesDirectory = Join-Path $PublishDirectory "Notices"
$ReleaseNotesPath = Join-Path $TemporaryRoot "release-notes.md"

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

function Test-PeHasCertificate {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a Windows PE file: $Path" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadUInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid Windows PE signature: $Path" }
        $optionalHeader = $peOffset + 24
        $stream.Position = $optionalHeader
        $magic = $reader.ReadUInt16()
        $dataDirectory = switch ($magic) {
            0x10B { $optionalHeader + 96 }
            0x20B { $optionalHeader + 112 }
            default { throw "Unknown Windows PE optional header: $Path" }
        }
        $stream.Position = $dataDirectory + (4 * 8)
        $certificateOffset = $reader.ReadUInt32()
        $certificateSize = $reader.ReadUInt32()
        return $certificateOffset -ne 0 -and $certificateSize -ne 0
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string] $BasePath,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $baseUri = [Uri]((([IO.Path]::GetFullPath($BasePath)).TrimEnd('\') + '\'))
    $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', '\')
}

function Assert-ReleaseInputs {
    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "Version must be a SemVer-compatible value such as 1.0.0 or 1.0.0-rc.1."
    }

    $repository = $null
    if (-not [Uri]::TryCreate($RepositoryUrl, [UriKind]::Absolute, [ref] $repository) `
        -or $repository.Scheme -ne "https" `
        -or $repository.Host -ne "github.com" `
        -or $repository.UserInfo `
        -or $repository.Query `
        -or $repository.Fragment `
        -or $repository.AbsolutePath.Trim('/').Split('/').Count -ne 2) {
        throw "RepositoryUrl must be exactly https://github.com/<owner>/<repository>."
    }

    $artifactPrefix = $ArtifactsRoot.TrimEnd('\') + '\'
    if (-not $OutputDirectory.StartsWith($artifactPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "OutputDirectory must be a child of the repository artifacts directory."
    }

    $vpkInformation = (Get-Item $VpkPath).VersionInfo.ProductVersion
    if ($vpkInformation -notmatch "^$([Regex]::Escape($VpkVersion))(\+|$)") {
        throw "Expected Velopack CLI $VpkVersion, got '$vpkInformation'."
    }

    if (-not $AllowDirtySource) {
        $trackedStatus = (& git -C $RepositoryRoot status --porcelain --untracked-files=no)
        if ($LASTEXITCODE -ne 0 -or $trackedStatus) {
            throw "Tracked source is dirty. Commit the verified phase or use -AllowDirtySource for local validation only."
        }
    }

    if ([string]::IsNullOrWhiteSpace($ProjectLicenseFile)) {
        if (-not $AllowMissingProjectLicense) {
            throw "A project-owner-approved software license is required for a public release."
        }
    }
    else {
        $script:ProjectLicenseFile = [IO.Path]::GetFullPath($ProjectLicenseFile)
        if (-not (Test-Path $script:ProjectLicenseFile -PathType Leaf)) {
            throw "ProjectLicenseFile does not exist."
        }
    }
}

function Copy-ReleaseNotices {
    New-Item -ItemType Directory -Path $NoticesDirectory -Force | Out-Null
    Copy-Item (Join-Path $RepositoryRoot "NOTICE.md") (Join-Path $NoticesDirectory "NOTICE.md")
    Copy-Item (Join-Path $RepositoryRoot "THIRD_PARTY_NOTICES.md") (Join-Path $NoticesDirectory "THIRD_PARTY_NOTICES.md")
    Copy-Item (Join-Path $RepositoryRoot "assets\windows\README.md") (Join-Path $NoticesDirectory "ASSET-PROVENANCE.md")
    Copy-Item (Join-Path $RepositoryRoot "docs\release\windows.md") (Join-Path $NoticesDirectory "WINDOWS-RELEASE.md")
    if (-not [string]::IsNullOrWhiteSpace($ProjectLicenseFile)) {
        Copy-Item $ProjectLicenseFile (Join-Path $NoticesDirectory "PROJECT-LICENSE.txt")
    }
}

function Write-DependencyManifest {
    $assetsPath = Join-Path $RepositoryRoot "src\Terraria_Wiki.Windows\obj\project.assets.json"
    if (-not (Test-Path $assetsPath)) {
        throw "NuGet assets are missing after publish."
    }
    $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
    $packageFolder = @($assets.packageFolders.PSObject.Properties.Name)[0]
    $nuget = @()
    foreach ($property in ($assets.libraries.PSObject.Properties | Sort-Object Name)) {
        if ($property.Value.type -ne "package") { continue }
        $parts = $property.Name.Split('/', 2)
        $packageId = $parts[0]
        $packageVersion = $parts[1]
        $packageRoot = Join-Path $packageFolder ($property.Value.path.Replace('/', '\'))
        $nuspecPath = Get-ChildItem $packageRoot -Filter "*.nuspec" -File | Select-Object -First 1
        $license = $null
        $projectUrl = $null
        $authors = $null
        $licenseFiles = @()
        if ($null -ne $nuspecPath) {
            [xml] $nuspec = Get-Content $nuspecPath.FullName -Raw
            $metadata = $nuspec.SelectSingleNode("//*[local-name()='metadata']")
            $licenseNode = $metadata.SelectSingleNode("./*[local-name()='license']")
            $licenseUrlNode = $metadata.SelectSingleNode("./*[local-name()='licenseUrl']")
            $projectUrlNode = $metadata.SelectSingleNode("./*[local-name()='projectUrl']")
            $authorsNode = $metadata.SelectSingleNode("./*[local-name()='authors']")
            if ($null -ne $licenseNode) {
                $license = $licenseNode.InnerText.Trim()
                if ($licenseNode.GetAttribute("type") -eq "file") {
                    $declaredFile = Join-Path $packageRoot $license
                    if (Test-Path $declaredFile -PathType Leaf) { $licenseFiles += $declaredFile }
                }
            }
            elseif ($null -ne $licenseUrlNode) { $license = $licenseUrlNode.InnerText.Trim() }
            if ($null -ne $projectUrlNode) { $projectUrl = $projectUrlNode.InnerText.Trim() }
            if ($null -ne $authorsNode) { $authors = $authorsNode.InnerText.Trim() }
        }
        $licenseFiles += @(Get-ChildItem $packageRoot -File | Where-Object {
            $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE)(\.|$)'
        } | Select-Object -ExpandProperty FullName)
        $licenseFiles = @($licenseFiles | Sort-Object -Unique)
        $copied = @()
        if ($licenseFiles.Count -gt 0) {
            $licenseDirectory = Join-Path $NoticesDirectory ("NuGet\" + $packageId + "-" + $packageVersion)
            New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
            foreach ($file in $licenseFiles) {
                $destination = Join-Path $licenseDirectory ([IO.Path]::GetFileName($file))
                Copy-Item $file $destination -Force
                $copied += Get-RelativePath $PublishDirectory $destination
            }
        }
        $nuget += [ordered]@{
            id = $packageId
            version = $packageVersion
            license = $license
            projectUrl = $projectUrl
            authors = $authors
            packagedLicenseFiles = @($copied)
        }
    }

    $pythonRoot = Join-Path $PublishDirectory "AgentRuntime\python\Lib\site-packages"
    $python = @()
    foreach ($metadataPath in (Get-ChildItem $pythonRoot -Directory -Filter "*.dist-info" | Sort-Object Name)) {
        $metadataFile = Join-Path $metadataPath.FullName "METADATA"
        $metadata = Get-Content $metadataFile
        $name = (($metadata | Where-Object { $_ -match '^Name: ' } | Select-Object -First 1) -replace '^Name: ', '')
        $packageVersion = (($metadata | Where-Object { $_ -match '^Version: ' } | Select-Object -First 1) -replace '^Version: ', '')
        $license = (($metadata | Where-Object { $_ -match '^License-(Expression): ' } | Select-Object -First 1) -replace '^License-Expression: ', '')
        if ([string]::IsNullOrWhiteSpace($license)) {
            $license = (($metadata | Where-Object { $_ -match '^License: ' } | Select-Object -First 1) -replace '^License: ', '')
        }
        $licenseFiles = @(Get-ChildItem $metadataPath.FullName -File -Recurse | Where-Object {
            $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE)(\.|$)'
        } | ForEach-Object { Get-RelativePath $PublishDirectory $_.FullName })
        $python += [ordered]@{
            name = $name
            version = $packageVersion
            license = $license
            packagedLicenseFiles = @($licenseFiles)
        }
    }

    $manifest = [ordered]@{
        schema = 1
        generatedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        nuget = @($nuget)
        python = @($python)
    }
    [IO.File]::WriteAllText(
        (Join-Path $NoticesDirectory "dependency-manifest.json"),
        ($manifest | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
}

function Assert-Payload {
    $forbiddenDirectories = @(".venv", "__pycache__", ".pytest_cache", ".mypy_cache", ".ruff_cache", "test", "tests")
    $forbiddenExtensions = @(".db", ".sqlite", ".pkg", ".pak", ".pyc", ".pyo", ".pdb", ".snk", ".pfx", ".p12")
    foreach ($file in (Get-ChildItem $PublishDirectory -File -Recurse -Force)) {
        $relative = Get-RelativePath $PublishDirectory $file.FullName
        $segments = $relative.Split('\')
        if (@($segments | Where-Object { $forbiddenDirectories -contains $_.ToLowerInvariant() }).Count -gt 0) {
            throw "Forbidden release directory: $relative"
        }
        if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) {
            throw "Forbidden release file type: $relative"
        }
    }

    foreach ($required in @(
        "Terraria_Wiki.Windows.exe",
        "ViewerAssets\index.html",
        "AgentRuntime\sidecar_launcher.py",
        "AgentRuntime\src\terraria_wiki_agent\sidecar.py",
        "AgentRuntime\python\python.exe",
        "AgentRuntime\python\runtime-manifest.json",
        "AgentRuntime\python\LICENSE.txt",
        "Notices\NOTICE.md",
        "Notices\THIRD_PARTY_NOTICES.md",
        "Notices\dependency-manifest.json")) {
        if (-not (Test-Path (Join-Path $PublishDirectory $required) -PathType Leaf)) {
            throw "Required release payload is missing: $required"
        }
    }

    $textExtensions = @(".json", ".md", ".txt", ".py", ".pth", ".toml", ".lock", ".html", ".css", ".js", ".svg", ".xml")
    $repositoryPattern = [Regex]::Escape($RepositoryRoot)
    $profilePattern = if ([string]::IsNullOrWhiteSpace($env:USERPROFILE)) { '(?!)' } else { [Regex]::Escape($env:USERPROFILE) }
    foreach ($file in (Get-ChildItem $PublishDirectory -File -Recurse | Where-Object {
        $textExtensions -contains $_.Extension.ToLowerInvariant() -and $_.Length -le 4MB
    })) {
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($content -match "(?i)$repositoryPattern" `
            -or $content -match "(?i)$profilePattern" `
            -or $content -match '(?i)(github_pat_|ghp_)[A-Za-z0-9_]+' `
            -or $content -match '(?i)sk-[A-Za-z0-9]{20,}') {
            throw "Potential secret or absolute user path in release payload: $(Get-RelativePath $PublishDirectory $file.FullName)"
        }
    }

    if (Test-PeHasCertificate (Join-Path $PublishDirectory "Terraria_Wiki.Windows.exe")) {
        throw "Phase 11 expects an unsigned application executable, but a PE certificate table was found."
    }
}

function Write-PayloadManifest {
    $files = @(Get-ChildItem $PublishDirectory -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = Get-RelativePath $PublishDirectory $_.FullName
            bytes = $_.Length
            sha256 = Get-Sha256 $_.FullName
        }
    })
    $manifest = [ordered]@{
        schema = 1
        version = $Version
        rid = $RuntimeIdentifier
        selfContained = $true
        singleFile = $false
        trimmed = $false
        aot = $false
        fileCount = $files.Count
        files = $files
    }
    [IO.File]::WriteAllText(
        (Join-Path $PublishDirectory "payload-manifest.json"),
        ($manifest | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))
}

function Assert-PackagedPortablePayload {
    param([Parameter(Mandatory = $true)][string] $PortablePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $verificationRoot = Join-Path $TemporaryRoot "portable-verification"
    [IO.Compression.ZipFile]::ExtractToDirectory($PortablePath, $verificationRoot)
    $payloadRoot = Join-Path $verificationRoot "current"
    $manifestPath = Join-Path $payloadRoot "payload-manifest.json"
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Portable payload manifest is missing."
    }

    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $path = Join-Path $payloadRoot $entry.path
        if (-not (Test-Path $path -PathType Leaf)) {
            throw "Portable payload file is missing: $($entry.path)"
        }
        $file = Get-Item $path
        if ($file.Length -ne $entry.bytes -or (Get-Sha256 $path) -ne $entry.sha256) {
            throw "Portable payload file does not match its manifest: $($entry.path)"
        }
    }

    $velopackMarker = Join-Path $payloadRoot "sq.version"
    if (-not (Test-Path $velopackMarker -PathType Leaf)) {
        throw "Portable Velopack version marker is missing."
    }
    $actualFiles = @(Get-ChildItem $payloadRoot -File -Recurse | Where-Object {
        $_.FullName -ne $manifestPath -and $_.FullName -ne $velopackMarker
    })
    if ($actualFiles.Count -ne $manifest.fileCount) {
        throw "Portable payload contains files not represented by its manifest."
    }
}

Assert-ReleaseInputs

try {
    New-Item -ItemType Directory -Path $PublishDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $PackageDirectory -Force | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($PreviousReleaseDirectory)) {
        $PreviousReleaseDirectory = [IO.Path]::GetFullPath($PreviousReleaseDirectory)
        if (-not (Test-Path $PreviousReleaseDirectory -PathType Container)) {
            throw "PreviousReleaseDirectory does not exist."
        }
        Get-ChildItem $PreviousReleaseDirectory -File | Where-Object {
            $_.Extension -eq ".nupkg" `
                -or $_.Name -like "RELEASES-*" `
                -or $_.Name -like "releases.*.json" `
                -or $_.Name -like "assets.*.json"
        } | Copy-Item -Destination $PackageDirectory
    }

    Invoke-Checked -FilePath $DotNetPath -Arguments @(
        "restore", $ProjectPath,
        "--locked-mode",
        "--nologo"
    )

    $dotnetArguments = @(
        "publish", $ProjectPath,
        "--configuration", "Release",
        "--runtime", $RuntimeIdentifier,
        "--self-contained", "true",
        "--no-restore",
        "-p:PublishProfile=$PublishProfile",
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version",
        "-p:RepositoryUrl=$RepositoryUrl",
        "--output", $PublishDirectory,
        "--nologo"
    )
    Invoke-Checked -FilePath $DotNetPath -Arguments $dotnetArguments

    $pythonDestination = Join-Path $PublishDirectory "AgentRuntime\python"
    if ([string]::IsNullOrWhiteSpace($BundledPythonSource)) {
        & (Join-Path $PSScriptRoot "Build-BundledPython.ps1") -OutputDirectory $pythonDestination -UvPath $UvPath
        if ($LASTEXITCODE -ne 0) { throw "Bundled Python build failed." }
    }
    else {
        $BundledPythonSource = [IO.Path]::GetFullPath($BundledPythonSource)
        if (-not (Test-Path (Join-Path $BundledPythonSource "runtime-manifest.json") -PathType Leaf)) {
            throw "BundledPythonSource is not a verified bundled runtime."
        }
        Copy-Item $BundledPythonSource $pythonDestination -Recurse
    }

    Remove-Item (Join-Path $PublishDirectory "*.pdb") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PublishDirectory "createdump.exe") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PublishDirectory "AgentRuntime\pyproject.toml") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $PublishDirectory "AgentRuntime\uv.lock") -Force -ErrorAction SilentlyContinue
    Copy-ReleaseNotices
    Write-DependencyManifest
    Assert-Payload
    Write-PayloadManifest

    [IO.File]::WriteAllText(
        $ReleaseNotesPath,
        "Terraria AI Wiki $Version Windows candidate.`r`n`r`nUnsigned self-contained win-x64 release. See bundled notices and WINDOWS-RELEASE.md before distribution.`r`n",
        [Text.UTF8Encoding]::new($false))

    $vpkArguments = @(
        "pack",
        "--outputDir", $PackageDirectory,
        "--channel", $Channel,
        "--runtime", $RuntimeIdentifier,
        "--packId", $PackId,
        "--packVersion", $Version,
        "--packDir", $PublishDirectory,
        "--packAuthors", "Terraria AI Wiki contributors",
        "--packTitle", "Terraria AI Wiki",
        "--releaseNotes", $ReleaseNotesPath,
        "--mainExe", "Terraria_Wiki.Windows.exe",
        "--icon", (Join-Path $RepositoryRoot "assets\windows\AppIcon\appicon.ico"),
        "--framework", "webview2",
        "--shortcuts", "StartMenuRoot",
        "--exclude", ".*\\.pdb"
    )
    if (-not [string]::IsNullOrWhiteSpace($ProjectLicenseFile)) {
        $vpkArguments += @("--instLicense", $ProjectLicenseFile)
    }
    Invoke-Checked $VpkPath @vpkArguments

    $setups = @(Get-ChildItem $PackageDirectory -File -Filter "*Setup.exe")
    $portables = @(Get-ChildItem $PackageDirectory -File -Filter "*Portable.zip")
    $fullPackages = @(Get-ChildItem $PackageDirectory -File -Filter "$PackId-$Version-$Channel-full.nupkg")
    if ($setups.Count -ne 1 -or $portables.Count -ne 1 -or $fullPackages.Count -ne 1) {
        throw "Velopack did not produce exactly one Setup, portable, and full package."
    }
    $setup = $setups[0]
    $portable = $portables[0]
    $fullPackage = $fullPackages[0]
    Assert-PackagedPortablePayload $portable.FullName
    foreach ($artifact in @($setup, $portable, $fullPackage)) {
        if ($artifact.Length -ge 2GB) { throw "Release artifact exceeds GitHub's 2 GiB limit: $($artifact.Name)" }
    }
    if (Test-PeHasCertificate $setup.FullName) {
        throw "Phase 11 expects an unsigned installer, but a PE certificate table was found."
    }

    if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Copy-Item (Join-Path $PackageDirectory "*") $OutputDirectory -Recurse

    $commit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw "Unable to resolve the source commit." }
    $pythonManifest = Get-Content (Join-Path $PublishDirectory "AgentRuntime\python\runtime-manifest.json") -Raw | ConvertFrom-Json
    $artifacts = @(Get-ChildItem $OutputDirectory -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = $_.Name; bytes = $_.Length; sha256 = Get-Sha256 $_.FullName }
    })
    $publicationReady = -not $AllowDirtySource -and -not $AllowMissingProjectLicense -and -not [string]::IsNullOrWhiteSpace($ProjectLicenseFile)
    $releaseManifest = [ordered]@{
        schema = 1
        version = $Version
        tag = "v$Version"
        commit = $commit
        repositoryUrl = $RepositoryUrl.TrimEnd('/')
        rid = $RuntimeIdentifier
        packId = $PackId
        channel = $Channel
        selfContained = $true
        evergreenWebView2Bootstrap = $true
        bundledPythonVersion = $pythonManifest.pythonVersion
        bundledPythonSha256 = $pythonManifest.pythonSha256
        unsigned = $true
        publicationReady = $publicationReady
        artifacts = $artifacts
    }
    $releaseManifestPath = Join-Path $OutputDirectory "release-manifest.json"
    [IO.File]::WriteAllText(
        $releaseManifestPath,
        ($releaseManifest | ConvertTo-Json -Depth 6),
        [Text.UTF8Encoding]::new($false))

    $checksumLines = @(Get-ChildItem $OutputDirectory -File | Where-Object {
        $_.Name -ne "SHA256SUMS.txt"
    } | Sort-Object Name | ForEach-Object { "$(Get-Sha256 $_.FullName)  $($_.Name)" })
    [IO.File]::WriteAllLines(
        (Join-Path $OutputDirectory "SHA256SUMS.txt"),
        $checksumLines,
        [Text.UTF8Encoding]::new($false))

    Write-Host "Windows release candidate created: $OutputDirectory"
    Write-Host "Publication ready: $publicationReady"
}
finally {
    if (Test-Path $TemporaryRoot) {
        Remove-Item $TemporaryRoot -Recurse -Force
    }
}
