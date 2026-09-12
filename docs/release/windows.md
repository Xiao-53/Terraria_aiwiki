# Windows release runbook

Archived Phase 11 established one self-contained `win-x64` payload that is packaged as a per-user Velopack
`Setup.exe`, a portable zip, and the full package/index required by application updates. GitHub
Releases is the only application distribution and update channel. Microsoft Store, MSIX, App
Installer, and Store identity are not used.

## Release inputs

- Windows 11 x64 build host with .NET 10 SDK.
- Velopack CLI `1.2.0`, matching the application package reference.
- Committed NuGet `packages.lock.json` files; release restore runs in locked mode.
- uv `0.12.9`.
- CPython `3.14.7` x64 embeddable ZIP from python.org with SHA-256
  `76c3c0384ab3f822486f32450f3a4d20f5d65ad0ec32ee34290971aa0eb817e6`.
- A confirmed SemVer, public `https://github.com/<owner>/<repo>` URL, and a project software
  license file approved by the project owner.

Run from the repository root in PowerShell:

```powershell
./src/Terraria_Wiki.Windows/Packaging/Build-WindowsRelease.ps1 `
  -Version 1.0.0 `
  -RepositoryUrl https://github.com/OWNER/REPOSITORY `
  -OutputDirectory ./artifacts/release/1.0.0 `
  -VpkPath ./artifacts/tools/vpk.exe `
  -UvPath ./artifacts/tools/uv.exe `
  -ProjectLicenseFile ./LICENSE
```

The script fails closed for a dirty tracked tree, a missing license, an unpinned tool, a bad source
hash, forbidden payload data, a signed/unsigned state mismatch, or inconsistent release metadata.
`-AllowDirtySource` and `-AllowMissingProjectLicense` exist only for local candidate validation;
their output manifest is marked `publicationReady: false`.

For `N → N+1`, pass the downloaded, checksum-verified previous release directory through
`-PreviousReleaseDirectory`. The packer retains the prior full package/index inputs and regenerates
the stable feed. Do not replace an existing GitHub release asset with different bytes.
Delta packages are patch inputs, not directly installable packages: Velopack must reconstruct and
verify the target full package before `Update.exe apply`. Applying a delta as though it were a full
package is invalid and can leave only patch files in `current`; repair with the trusted full Setup
if that operator error occurs.

## Install, repair, update, and uninstall

- Install by running the versioned `Setup.exe`. Evergreen WebView2 is a bootstrap prerequisite;
  the application itself remains self-contained for .NET and Python.
- Repair by rerunning the same trusted `Setup.exe`. This replaces application payload files.
- Check for updates explicitly under Settings → About and Updates. The client reads stable releases
  from the compiled public repository URL without a GitHub token and never silently downloads.
- Uninstall from Windows Installed Apps. Host settings, favorites, AI provider configuration,
  Credential Manager data, and the external Wiki data root are intentionally outside the install
  directory and are retained. Remove them separately only when the user explicitly wants that.
- Portable builds are extracted into a user-writable directory and do not register an installed
  application. If in-place update is unavailable, download the new portable zip from Releases.

## Unsigned release warning

Phase 11 does not configure commercial Authenticode signing. Windows SmartScreen, Smart App
Control, or enterprise policy may warn about or block each new binary. Publish SHA-256 checksums and
source provenance, but never describe hashes as code signing and never ask users to disable system
protection globally.

No script in this repository uploads, publishes, or modifies a GitHub Release. External publication
requires a separately authorized step after the local release manifest and regression evidence are
reviewed.
