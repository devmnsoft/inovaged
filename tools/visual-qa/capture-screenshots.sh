#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
if ! node -e "require.resolve('playwright')" >/dev/null 2>&1; then
  echo "Playwright não está instalado para Node. Execute: npm install --no-save playwright && npx playwright install chromium" >&2
  exit 2
fi
node tools/visual-qa/capture.mjs
