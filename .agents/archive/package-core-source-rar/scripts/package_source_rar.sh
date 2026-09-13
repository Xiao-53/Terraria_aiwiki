#!/usr/bin/env bash
set -euo pipefail

usage() {
    printf '%s\n' \
        'Usage: package_source_rar.sh [--dry-run] [--output-dir WSL_PATH] [--date YYYYMMDD]'
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
rar_exe='/mnt/c/Program Files/WinRAR/Rar.exe'
archive_name="terraria-aiwiki-core-source-${date_stamp}.rar"
archive_path="${output_dir%/}/${archive_name}"

mapfile -d '' tracked_paths < <(
    git -C "$repo_root" ls-files -z -- 'src/**' 'agent/src/**' |
        while IFS= read -r -d '' path; do
            case "$path" in
                src/*.cs|src/*.axaml|agent/src/*.py)
                    printf '%s\0' "$path"
                    ;;
            esac
        done |
        sort -z
)

((${#tracked_paths[@]} > 0)) || {
    printf '%s\n' 'No tracked product source matched the fixed allowlist.' >&2
    exit 3
}

eligible_untracked=()
while IFS= read -r -d '' path; do
    case "$path" in
        src/*.cs|src/*.axaml|agent/src/*.py)
            eligible_untracked+=("$path")
            ;;
    esac
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

[[ -x "$rar_exe" ]] || {
    printf 'WinRAR command not found or not executable: %s\n' "$rar_exe" >&2
    exit 6
}
[[ -d "$output_dir" ]] || {
    printf 'Output directory does not exist: %s\n' "$output_dir" >&2
    exit 7
}
[[ ! -e "$archive_path" ]] || {
    printf 'Refusing to overwrite existing archive: %s\n' "$archive_path" >&2
    exit 8
}

stage_dir="$(mktemp -d /tmp/terraria-aiwiki-core-source.XXXXXX)"
cleanup() {
    rm -rf -- "$stage_dir"
}
trap cleanup EXIT

for path in "${tracked_paths[@]}"; do
    (
        cd "$repo_root"
        cp --parents --preserve=mode,timestamps -- "$path" "$stage_dir"
    )
done

archive_windows="$(wslpath -w "$archive_path")"
(
    cd "$stage_dir"
    "$rar_exe" a -idq -r "$archive_windows" src agent
)
"$rar_exe" t -idq "$archive_windows"

entry_list="$stage_dir/archive-entries.txt"
"$rar_exe" lb "$archive_windows" >"$entry_list"
entry_count="$(sed 's/\r$//' "$entry_list" | awk 'NF { count += 1 } END { print count + 0 }')"

printf 'Archive entries: %s\n' "$entry_count"
printf 'SHA-256: %s\n' "$(sha256sum "$archive_path" | awk '{print $1}')"
printf '%s\n' 'Integrity: WinRAR test passed'
