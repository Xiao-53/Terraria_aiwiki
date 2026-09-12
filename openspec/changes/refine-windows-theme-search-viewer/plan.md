# Change: refine-windows-theme-search-viewer

## Why

Improve the current Windows presentation after the existing viewer/header change:
replace the local dark Wiki's black page background, synchronize the native title
bar with the app theme, place the Wiki search controls together at the right side
of the header, and remove the host iframe's bottom gap.

## Scope

- `MainWindow.cs`: synchronize Windows DWM caption, border, and text colors with
  the Avalonia theme.
- `ShellView.axaml`: keep the Wiki search field and search button as one
  right-aligned header group.
- `viewer-host.html`: make the iframe a block-level full-size surface.
- `theme/dark.css`: use `#5A433A` as the dark site background base color.

## Invariants

- Preserve the existing native window chrome and WebView2 bridge.
- Preserve Wiki search bindings, commands, automation IDs, and scrollbar
  behavior.
- Do not change Core, Application, search/index behavior, stored data, or the
  existing rollback point at `a4269de`.

## Validation

- Build the Windows Release target after the changes are committed.
- Do not run automated tests or self-acceptance; hand off the Release output for
  direct user acceptance.

## Status

ready-for-user-acceptance
