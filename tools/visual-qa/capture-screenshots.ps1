$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
Push-Location $Root
try {
  node -e "require.resolve('playwright')" 2>$null
  if ($LASTEXITCODE -ne 0) { throw "Playwright não está instalado. Execute: npm install --no-save playwright; npx playwright install chromium" }
  node tools/visual-qa/capture.mjs
  if ($LASTEXITCODE -ne 0) { throw "A captura encontrou rota inválida, erro 500 ou sessão ausente." }
} finally { Pop-Location }
