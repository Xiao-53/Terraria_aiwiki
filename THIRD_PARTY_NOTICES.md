# Third-party notices

The Windows release is self-contained and includes Microsoft .NET runtime files, NuGet packages,
CPython, Python packages, and a controlled viewer asset snapshot. Each release contains a generated
`Notices/dependency-manifest.json` with the exact packaged NuGet and Python distribution versions,
declared license metadata, project URLs, and any license files found in the restored package.

Major direct runtime components include:

- Microsoft .NET 10 runtime and ASP.NET Core shared runtime
- Avalonia, Avalonia Desktop, Avalonia Fluent Theme, and Avalonia Controls WebView
- Velopack
- Markdig
- Model Context Protocol .NET SDK
- Html Agility Pack
- sqlite-net-pcl and its native SQLite dependencies
- CPython embeddable runtime
- `pydantic-ai-slim[mcp,openai]` and the exact transitive Python packages frozen by `agent/uv.lock`
- Viewer.js and handy-scroll.js in the controlled viewer snapshot

Standard license texts and package-specific notices remain alongside the Python `.dist-info`
metadata where supplied. CPython's `LICENSE.txt` remains at `AgentRuntime/python/LICENSE.txt`.
Viewer asset attribution remains in the bundled HTML/JavaScript and in
`Notices/ASSET-PROVENANCE.md`.

This inventory is a distribution aid, not a new license grant and not a substitute for reviewing
the terms of every component before public release.
