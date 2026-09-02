# Windows bundled asset snapshot

This directory is the product-owned build snapshot for the Windows application. Normal build,
publish, and runtime paths must not read `terraria-wiki-offline/`.

## Provenance

- Upstream repository: `https://github.com/BigBearKing/terraria-wiki-offline.git`
- Upstream reference commit: `a1dd89981d7604006ec91955d4d19e93cb933ce1`
- Product snapshot commit: `d67d904c32b853edb664b68121897a2f2a8f2655`
- Outer migration commit: the commit containing this file, with subject
  `refactor(repo): establish standalone application baseline`
- Source paths:
  - `Terraria_Wiki/Resources/Raw/Web/**` → `Viewer/Web/**` (91 files)
  - `Terraria_Wiki/wwwroot/js/iframe-bridge.js` → `Viewer/_common/iframe-bridge.js`
  - `Terraria_Wiki/Resources/AppIcon/{logo.svg,appicon.ico}` → `AppIcon/`

The 94 files copied into this directory are byte-identical to the product snapshot. Together
with `src/Terraria_Wiki.Windows/Assets/viewer-host.html` and
`src/Terraria_Wiki.Windows/app.manifest`, they form the 96-file Windows Viewer/icon/host support
set verified during the migration. All external snapshot files match upstream `a1dd899` except
`Viewer/Web/_common/wiki-app-common.js`, whose product copy comes from `d67d904` and includes the
later bounded lifecycle behavior introduced by product commit `5d1d52f`.

For the migration checkout, the SHA-256 of the sorted per-file SHA-256 manifest is
`5ac242971b3781303b8ee52c6105da31ee6e8d4eb42382658c431c3ed32e443a` for the 94-file snapshot and
`59a57f6e08a6ad0a9cb37d48cc7abf8d7eba1fbee01a8ebad2493e642fe35231` for the complete 96-file
support set. Paths are relative to the repository root and participate in the manifest, so a
rename or content change alters the recorded value.

## Copyright and licensing notes

This is a relocation of assets already bundled by the reference application, not a new license
grant. The upstream tree at `a1dd899` has no standalone `LICENSE` file. Its README states that
Terraria and related art are owned by Re-Logic, Wiki text and image data are distributed under
CC BY-NC-SA 4.0, the client is free/non-commercial, and the application is unaffiliated with
wiki.gg or Re-Logic.

Some bundled pages carry more specific notices that remain intact in their HTML, including
CC BY-NC-SA 3.0 notices for Calamity Wiki content. The bundled Viewer.js files retain their MIT
copyright headers. `handy-scroll.js` and Viewer.js are attributed by the upstream README. These
notices and embedded attributions must not be stripped when the snapshot is updated or packaged.

Before any public or commercial distribution, the maintainer must independently confirm that the
upstream non-commercial statement and every third-party/game asset permit the intended release.
