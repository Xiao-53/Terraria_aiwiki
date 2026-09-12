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

The initial 94 files copied into this directory were byte-identical to the product snapshot.
On 2026-09-03, `AppIcon/appicon.ico` became a product-owned override generated from the image
supplied by the project owner. The current 94-file runtime asset set therefore consists of 93
retained snapshot files and the generated icon. `AppIcon/appicon-source.png` preserves the
1254×1254 RGBA source for regeneration and is not embedded or published as a runtime asset.

The icon contains 256, 64, 48, 32, and 16 pixel 32-bit RGBA PNG-compressed frames generated with
System.Drawing high-quality bicubic resizing. Its source SHA-256 is
`7892172063d661a2ee77b389b105bcf201df4f80682cf7e66b9dae9bbbcd16b8`; the generated ICO SHA-256 is
`cfb581e78a1a1682c4e0593cf56d5f567f14e0c79f677ae56859c8dbb26e0518`.

Together with `src/Terraria_Wiki.Windows/Assets/viewer-host.html` and
`src/Terraria_Wiki.Windows/app.manifest`, the runtime assets form the 96-file Windows
Viewer/icon/host support set. All retained external snapshot files match upstream `a1dd899`
except `Viewer/Web/_common/wiki-app-common.js`, whose product copy comes from `d67d904` and
includes the later bounded lifecycle behavior introduced by product commit `5d1d52f`. The current
product override additionally reports a one-time read token and navigation version only after the
latest page DOM is committed, allowing the Windows host to publish trusted canonical page identity;
its SHA-256 is `c5cd8fac92a395df8685f1392d9ec3864be6b4178d46782bf2f35c20e0bb02e8`.

For the current checkout, hashing the LF-delimited sequence of per-file SHA-256 values in ordinal
repository-path order gives `ca4f78dbc48ffa2d2b61c7324ca5b5534bc777598c9782b3d583ed7e78c2f27c`
for the 94-file runtime asset set and
`abecefdca1496042d2c88d6d078dfe7fd1cf2761ffea73c1b3e8146fece28c98` for the complete 96-file
support set. The original migration values and the pre-commit product values remain recorded in
ADR 0007 and the archived migration/icon plans. Paths determine ordering but are not themselves
hashed; content changes alter the recorded value.

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
upstream non-commercial statement, every third-party/game asset, and the project-owner-supplied
application icon permit the intended release. The upstream licensing statements do not grant
rights to the replacement application icon.
