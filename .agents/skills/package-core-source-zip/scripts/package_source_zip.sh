#!/usr/bin/env bash
set -euo pipefail

usage() {
    printf '%s\n' \
        'Usage: package_source_zip.sh [--dry-run] [--output-dir WSL_PATH] [--date YYYYMMDD]'
}

dry_run=false
output_dir='/mnt/c/Users/Xiao/Desktop'
date_stamp="$(date +%Y%m%d)"

while (($# > 0)); do
    case "$1" in
        --dry-run)
            dry_run=true
            shift
            ;;
        --output-dir)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            output_dir="$2"
            shift 2
            ;;
        --date)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            date_stamp="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

[[ "$date_stamp" =~ ^[0-9]{8}$ ]] || {
    printf 'Date must use YYYYMMDD: %s\n' "$date_stamp" >&2
    exit 2
}

repo_root="$(git rev-parse --show-toplevel)"
archive_name="terraria-aiwiki-core-source-${date_stamp}.zip"
archive_path="${output_dir%/}/${archive_name}"

mapfile -d '' tracked_paths < <(
    git -C "$repo_root" ls-files -z -- 'src/**' 'agent/src/**' |
        while IFS= read -r -d '' path; do
            if [[ "$path" =~ ^src/.*\.(cs|axaml)$ || "$path" =~ ^agent/src/.*\.py$ ]]; then
                printf '%s\0' "$path"
            fi
        done |
        sort -z
)

((${#tracked_paths[@]} > 0)) || {
    printf '%s\n' 'No tracked product source matched the fixed allowlist.' >&2
    exit 3
}

eligible_untracked=()
while IFS= read -r -d '' path; do
    if [[ "$path" =~ ^src/.*\.(cs|axaml)$ || "$path" =~ ^agent/src/.*\.py$ ]]; then
        eligible_untracked+=("$path")
    fi
done < <(git -C "$repo_root" ls-files --others --exclude-standard -z -- 'src/**' 'agent/src/**')

if ((${#eligible_untracked[@]} > 0)); then
    printf '%s\n' 'Untracked eligible source would be omitted; stopping:' >&2
    printf '  %s\n' "${eligible_untracked[@]}" >&2
    exit 4
fi

for path in "${tracked_paths[@]}"; do
    [[ -f "$repo_root/$path" && ! -L "$repo_root/$path" ]] || {
        printf 'Allowed path is missing, not a regular file, or a symlink: %s\n' "$path" >&2
        exit 5
    }
done

mapfile -t modified_source < <(
    git -C "$repo_root" status --short --untracked-files=no -- 'src/**' 'agent/src/**'
)

printf 'Repository: %s\n' "$repo_root"
printf 'Commit: %s\n' "$(git -C "$repo_root" rev-parse HEAD)"
printf 'Source files: %d\n' "${#tracked_paths[@]}"
printf 'Output: %s\n' "$archive_path"
if ((${#modified_source[@]} > 0)); then
    printf '%s\n' 'Modified tracked source included from the working tree:'
    printf '  %s\n' "${modified_source[@]}"
else
    printf '%s\n' 'Modified tracked source: none'
fi

if [[ "$dry_run" == true ]]; then
    printf '%s\n' 'Dry run: manifest validated; no archive created.'
    exit 0
fi

for command_name in powershell.exe wslpath python3; do
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'Required command not found: %s\n' "$command_name" >&2
        exit 6
    }
done
[[ -d "$output_dir" ]] || {
    printf 'Output directory does not exist: %s\n' "$output_dir" >&2
    exit 7
}
[[ ! -e "$archive_path" ]] || {
    printf 'Refusing to overwrite existing archive: %s\n' "$archive_path" >&2
    exit 8
}

stage_dir="$(mktemp -d /tmp/terraria-aiwiki-core-source.XXXXXX)"
helper_dir="$(mktemp -d /tmp/terraria-aiwiki-zip-helper.XXXXXX)"
cleanup() {
    rm -rf -- "$stage_dir" "$helper_dir"
}
trap cleanup EXIT

for path in "${tracked_paths[@]}"; do
    (
        cd "$repo_root"
        cp --parents --preserve=mode,timestamps -- "$path" "$stage_dir"
    )
done

manifest_path="$helper_dir/manifest.txt"
printf '%s\n' "${tracked_paths[@]}" >"$manifest_path"

ps_script="$helper_dir/create_zip.ps1"
cat >"$ps_script" <<'POWERSHELL'
param(
    [Parameter(Mandatory = $true)][string]$StageDir,
    [Parameter(Mandatory = $true)][string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$files = @(Get-ChildItem -LiteralPath $StageDir -File -Recurse | Sort-Object FullName)
if ($files.Count -eq 0) { throw 'Nothing to archive.' }

$stream = [System.IO.File]::Open($ArchivePath, [System.IO.FileMode]::CreateNew)
$archive = [System.IO.Compression.ZipArchive]::new(
    $stream,
    [System.IO.Compression.ZipArchiveMode]::Create,
    $false
)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($StageDir.Length + 1).Replace('\', '/')
        $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
        $input = [System.IO.File]::OpenRead($file.FullName)
        try {
            $target = $entry.Open()
            try { $input.CopyTo($target) }
            finally { $target.Dispose() }
        }
        finally { $input.Dispose() }
    }
}
finally {
    $archive.Dispose()
    $stream.Dispose()
}
POWERSHELL

stage_windows="$(wslpath -w "$stage_dir")"
archive_windows="$(wslpath -w "$archive_path")"
ps_script_windows="$(wslpath -w "$ps_script")"

powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass \
    -File "$ps_script_windows" \
    -StageDir "$stage_windows" \
    -ArchivePath "$archive_windows" >/dev/null

python3 - "$archive_path" "$manifest_path" <<'PY'
from pathlib import Path
import sys
import zipfile

archive_path = Path(sys.argv[1])
manifest_path = Path(sys.argv[2])
expected = [line.rstrip("\n") for line in manifest_path.read_text(encoding="utf-8").splitlines() if line]

with zipfile.ZipFile(archive_path, "r") as archive:
    bad_entry = archive.testzip()
    if bad_entry is not None:
        raise SystemExit(f"ZIP CRC verification failed at: {bad_entry}")

    actual = [
        info.filename.replace("\\", "/").rstrip("/")
        for info in archive.infolist()
        if not info.is_dir()
    ]

if len(actual) != len(set(actual)):
    raise SystemExit("ZIP verification failed: duplicate file entries detected")

if sorted(actual) != sorted(expected):
    expected_set = set(expected)
    actual_set = set(actual)
    missing = sorted(expected_set - actual_set)
    extra = sorted(actual_set - expected_set)
    if missing:
        print("Missing archive entries:", file=sys.stderr)
        for path in missing:
            print(f"  {path}", file=sys.stderr)
    if extra:
        print("Unexpected archive entries:", file=sys.stderr)
        for path in extra:
            print(f"  {path}", file=sys.stderr)
    raise SystemExit("ZIP verification failed: archive manifest differs from source manifest")

print(f"Archive entries: {len(actual)}")
print("Integrity: ZIP CRC and manifest verification passed")
PY

printf 'SHA-256: %s\n' "$(sha256sum "$archive_path" | awk '{print $1}')"
