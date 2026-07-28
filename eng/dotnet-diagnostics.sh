#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"; out="$root/artifacts/diagnostics"; mkdir -p "$out"
capture() { "$@" 2>&1 || true; }
os="$(uname -sr)"; arch="$(uname -m)"; selected="$(cd "$root" && capture dotnet --version)"
sdks="$(capture dotnet --list-sdks)"; runtimes="$(capture dotnet --list-runtimes)"; info="$(capture dotnet --info)"
tfms="$(find "$root" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' -exec sed -n 's:.*<TargetFrameworks\{0,1\}>\([^<]*\).*:\1:p' {} \; | sort -u | paste -sd, -)"
python3 - "$out/dotnet-environment.json" "$os" "$arch" "${DOTNET_ROOT:-}" "$selected" "$root/global.json" "$tfms" "$info" "$sdks" "$runtimes" <<'PY'
import json,sys
keys=['os','architecture','dotnetRoot','selectedSdk','globalJson','targetFrameworks','dotnetInfo','sdks','runtimes']
with open(sys.argv[1],'w',encoding='utf-8') as f: json.dump(dict(zip(keys,sys.argv[2:])),f,ensure_ascii=False,indent=2)
PY
{
  printf 'OS: %s\nArchitecture: %s\nDOTNET_ROOT: %s\nSelected SDK: %s\nglobal.json: %s\nTargetFrameworks: %s\nMSBuild/NuGet: included in dotnet --info\nVisual Studio: not detectable on this host\n\n-- dotnet --info --\n%s\n\n-- SDKs --\n%s\n\n-- runtimes --\n%s\n' "$os" "$arch" "${DOTNET_ROOT:-not set}" "$selected" "$root/global.json" "$tfms" "$info" "$sdks" "$runtimes"
} > "$out/dotnet-environment.txt"
printf 'Diagnóstico seguro criado em %s\n' "$out"
