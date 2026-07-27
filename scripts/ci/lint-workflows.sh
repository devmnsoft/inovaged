#!/usr/bin/env bash
set -euo pipefail

readonly root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly version="$(tr -d '[:space:]' < "$root_dir/scripts/ci/actionlint-version.txt")"
readonly os="$(uname -s | tr '[:upper:]' '[:lower:]')"
case "$(uname -m)" in
  x86_64) arch=x86_64 ;;
  aarch64|arm64) arch=arm64 ;;
  *) echo "Unsupported actionlint architecture: $(uname -m)" >&2; exit 2 ;;
esac
readonly archive="actionlint_${version#v}_${os}_${arch}.tar.gz"
readonly cache_dir="${ACTIONLINT_CACHE_DIR:-$root_dir/.cache/actionlint/$version}"
readonly binary="$cache_dir/actionlint"

if [[ ! -x "$binary" ]]; then
  mkdir -p "$cache_dir"
  curl --fail --silent --show-error --location \
    "https://github.com/rhysd/actionlint/releases/download/$version/$archive" \
    --output "$cache_dir/$archive"
  curl --fail --silent --show-error --location \
    "https://github.com/rhysd/actionlint/releases/download/$version/actionlint_${version#v}_checksums.txt" \
    --output "$cache_dir/checksums.txt"
  expected="$(awk -v file="$archive" '$2 == file { print $1 }' "$cache_dir/checksums.txt")"
  [[ "$expected" =~ ^[0-9a-fA-F]{64}$ ]] || { echo "Checksum not published for $archive" >&2; exit 3; }
  printf '%s  %s\n' "$expected" "$cache_dir/$archive" | sha256sum --check --status
  tar -xzf "$cache_dir/$archive" -C "$cache_dir" actionlint
fi

"$binary" -verbose -color never "$root_dir/.github/workflows/inovaged-ci.yml"
