#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
global="$root/global.json"
fail() { printf '\nToolchain do InovaGED não encontrada.\n\nFramework de destino: net8.0\nSDK necessário: .NET SDK 8.0\nSDK selecionado: %s\n\nInstale o .NET SDK 8 utilizando o repositório oficial\nda sua distribuição ou uma imagem de build homologada.\n' "${1:-não detectado}" >&2; exit 1; }
command -v dotnet >/dev/null 2>&1 || fail "não detectado"
[[ -f "$global" ]] || { echo "global.json não encontrado em $root" >&2; exit 2; }
python3 - "$global" <<'PY' || { echo "global.json inválido ou fora do contrato 8.0.100/latestFeature." >&2; exit 2; }
import json,sys
d=json.load(open(sys.argv[1], encoding='utf-8'))['sdk']
assert d['version']=='8.0.100' and d['rollForward']=='latestFeature' and d['allowPrerelease'] is False
PY
sdks="$(dotnet --list-sdks 2>/dev/null || true)"
selected="$(cd "$root" && dotnet --version 2>/dev/null || true)"
printf '%s\n' "$sdks" | grep -Eq '^8\.0\.[0-9]+ ' || fail "$selected"
[[ "$selected" =~ ^8\.0\.[0-9]+$ ]] || fail "$selected"
printf 'Toolchain do InovaGED validada: SDK %s (global.json: 8.0.100/latestFeature).\n' "$selected"
