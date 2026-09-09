# Phase 00 Host Spike

This directory is a disposable, non-production characterization probe for the
Windows host decision. It does not move or replace the existing Terraria Wiki
Core. The source repository under `../../terraria-wiki-offline` is only linked
as a read-only build input for the Viewer assets.

The projects target only `net10.0` (probes/runner/tests) and
`net10.0-windows10.0.17763.0` (the Avalonia host). No Android, iOS, Linux,
macOS, browser, MAUI, or workload target is declared here.

Typical Windows commands from this directory are:

```powershell
pwsh ./scripts/restore.ps1
pwsh ./scripts/build.ps1
pwsh ./scripts/test.ps1
dotnet run --project ./src/Phase00.ProbeRunner -- fixture --output ./artifacts/fixture.json
dotnet run --project ./src/Phase00.TlsProbe -- --url https://terraria.wiki.gg/api.php --output ./artifacts/tls.json
pwsh ./scripts/publish.ps1
pwsh ./scripts/run-smoke.ps1 -SmokeLog ./artifacts/smoke.json
pwsh ./scripts/sandbox-smoke.ps1
pwsh ./scripts/sandbox-smoke.ps1 -ExpectedWebView2State Missing
pwsh ./scripts/sandbox-smoke.ps1 -WebView2RuntimeFolder 'C:\path\to\fixed-webview2-runtime'
```

`--database` and `--package` are always explicit. A real database or package
is never an implicit default and is never an output target. Database probes
copy a supplied database into a temporary directory before any write test;
package probes stream the package into a temporary staging directory and
remove it on completion. Pass `--keep-temp true` only when an explicit
forensic inspection of that disposable copy/staging is needed. The runner
never writes package payloads to the workspace output directory.

The TLS probe records status, byte count, duration, and assembly versions but
never writes the response body or URL query to its JSON evidence.

`sandbox-smoke.ps1` publishes a self-contained Windows build, maps only the
disposable publish/evidence folder into Windows Sandbox, runs the automatic
NativeWebView smoke without networking, records the clean environment, and
shuts the Sandbox down. It requires the Windows Sandbox optional feature. The
default `Available` expectation requires the full Viewer smoke to pass. Use the
explicit `Missing` expectation only to prove that a clean machine without the
Evergreen WebView2 Runtime still launches the self-contained host and reports
the missing prerequisite observably; that result is not a Viewer success. The
optional fixed-runtime folder is mapped read-only only for a clean-environment
`Available` smoke; it is verification input, not a committed runtime bundle.

The Viewer files are copied from the existing source repository at build time
into ignored output directories. They are not duplicated as maintained source
assets in this experiment.
