#!/usr/bin/env bash
set -euo pipefail
version=${1:?Uso: $0 VERSION [OUTPUT]}; output=${2:-artifacts/release}; root=$(cd "$(dirname "$0")/../.." && pwd); cd "$root"
[[ ${INOVAGED_UNOFFICIAL:-false} == true ]] || [[ -z $(git status --porcelain) ]] || { echo 'Build oficial exige repositório limpo.' >&2; exit 2; }
# O Bash é deliberadamente limitado a build/verificação; nunca administra IIS.
pwsh eng/release/New-InovaGedRelease.ps1 -Version "$version" -OutputDirectory "$output" ${INOVAGED_UNOFFICIAL:+-Unofficial}
