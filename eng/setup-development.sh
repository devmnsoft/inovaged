#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"; cd "$root"
bash eng/verify-dotnet-sdk.sh
command -v git >/dev/null || { echo 'Git não encontrado.' >&2; exit 2; }
dotnet restore InovaGed.sln
echo 'Configure segredos com dotnet user-secrets; não grave credenciais no repositório.'
dotnet run --project InovaGed.Environment.Doctor -- check || code=$?; code=${code:-0}; [[ $code -le 1 ]] || exit "$code"
echo 'Para aplicar migrations, use o InovaGed.Database.Migrator conforme a documentação.'
if [[ "${1:-}" == '--build' ]]; then bash eng/build.sh; fi
