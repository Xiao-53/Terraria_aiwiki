---
name: package-core-source-zip
description: "Create a verified source-only ZIP backup for Terraria AI Wiki from the tracked C#, Avalonia XAML, and Python product sources. Use when the user asks to back up or package the repository's core source as a Windows ZIP; do not use for build, release, data, assets, tests, or a complete reproducible-source distribution."
---

# Package Core Source ZIP

Create a backup of the current working-tree bytes for the repository's tracked product source without changing or deleting repository content. The archive is created with Windows' built-in PowerShell `Compress-Archive`; WinRAR is not required.

## Fixed boundary

- Include only tracked `src/**/*.cs`, `src/**/*.axaml`, and `agent/src/**/*.py` files.
- Exclude tests, assets, project/solution/configuration files, documentation, plans, databases, data packages, release artifacts, caches, virtual environments, secrets, Git metadata, and untracked files.
- Treat untracked files matching the allowed source paths/extensions as a hard stop so a backup cannot silently omit candidate source.
- Include modified tracked source using its current working-tree bytes, and report that the archive is not an exact committed revision when applicable.
- Write temporary staging only below `/tmp`. Write the final ZIP outside the repository, normally to the Windows desktop. Never overwrite an existing archive.
- Match the allowlist recursively under `src/` and `agent/src/`; do not silently limit the backup to files directly below either directory.

## Workflow

1. Read the root `AGENTS.md`, inspect `git status --short`, and confirm the requested output directory/name. Creating the external archive must remain within the user's current authorization.
2. Run a non-writing manifest check first:

   ```bash
   bash .agents/skills/package-core-source-zip/scripts/package_source_zip.sh --dry-run
   ```

3. Review the reported count and any modified tracked source. If untracked eligible source is reported, stop and ask whether it should first become tracked; do not broaden the allowlist automatically.
4. Create and verify the archive. The default output is `/mnt/c/Users/Xiao/Desktop/terraria-aiwiki-core-source-YYYYMMDD.zip`:

   ```bash
   bash .agents/skills/package-core-source-zip/scripts/package_source_zip.sh
   ```

   Use `--output-dir <WSL-path>` or `--date <YYYYMMDD>` only when requested. The script invokes the Windows PowerShell/.NET ZIP implementation, writes each staged source file exactly once, verifies the resulting ZIP CRCs and manifest with Python's standard-library `zipfile`, and prints SHA-256. It does not require WinRAR, 7-Zip, or another archive utility.
5. Report the exact archive path, source-file count, archive-entry count, SHA-256, integrity result, current commit, and whether tracked source was modified. Do not call this a build, release, or complete runnable source package.

If historical boundary evidence is needed, consult the repository's prior archive/package plan rather than widening this Skill's fixed allowlist.
