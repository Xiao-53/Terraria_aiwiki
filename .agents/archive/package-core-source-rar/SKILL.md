---
name: package-core-source-rar
description: "Create a verified source-only RAR backup for Terraria AI Wiki from the tracked C#, Avalonia XAML, and Python product sources. Use when the user asks to back up or package the repository's core source as RAR; do not use for build, release, data, assets, tests, or a complete reproducible-source distribution."
---

# Package Core Source RAR

Create a backup of the current working-tree bytes for the repository's tracked product source without changing or deleting repository content.

## Fixed boundary

- Include only tracked `src/**/*.cs`, `src/**/*.axaml`, and `agent/src/**/*.py` files.
- Exclude tests, assets, project/solution/configuration files, documentation, plans, databases, data packages, release artifacts, caches, virtual environments, secrets, Git metadata, and untracked files.
- Treat untracked files matching the allowed source paths/extensions as a hard stop so a backup cannot silently omit candidate source.
- Include modified tracked source using its current working-tree bytes, and report that the archive is not an exact committed revision when applicable.
- Write temporary staging only below `/tmp`. Write the final RAR outside the repository, normally to the Windows desktop. Never overwrite an existing archive.

## Workflow

1. Read the root `AGENTS.md`, inspect `git status --short`, and confirm the requested output directory/name. Creating the external archive must remain within the user's current authorization.
2. Run a non-writing manifest check first:

   ```bash
   bash .agents/skills/package-core-source-rar/scripts/package_source_rar.sh --dry-run
   ```

3. Review the reported count and any modified tracked source. If untracked eligible source is reported, stop and ask whether it should first become tracked; do not broaden the allowlist automatically.
4. Create and verify the archive. The default output is `/mnt/c/Users/Xiao/Desktop/terraria-aiwiki-core-source-YYYYMMDD.rar`:

   ```bash
   bash .agents/skills/package-core-source-rar/scripts/package_source_rar.sh
   ```

   Use `--output-dir <WSL-path>` or `--date <YYYYMMDD>` only when requested. The script requires Windows WinRAR at `/mnt/c/Program Files/WinRAR/Rar.exe`, stages the allowlist, runs the RAR integrity test, lists archive entries, and prints SHA-256.
5. Report the exact archive path, source-file count, archive-entry count, SHA-256, integrity result, current commit, and whether tracked source was modified. Do not call this a build, release, or complete runnable source package.

The historical implementation and boundary evidence are in `openspec/changes/archive/package-core-source-rar/plan.md`.
