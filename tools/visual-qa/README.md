# Visual QA — Administração e Etiquetas

Rotina auxiliar (fora do build) para capturar todas as rotas de `routes.json` em **1366×768**, **1920×1080** e, nas entradas mobile, **390×844**. Qualquer resposta HTTP 500+ falha a execução. As imagens são gravadas em `artifacts/visual-qa/screenshots`.

## Preparação e execução

```bash
npm install --no-save playwright
npx playwright install chromium
VISUAL_QA_BASE_URL=http://localhost:5000 \
VISUAL_QA_EMAIL=admin@inovaged.local \
VISUAL_QA_PASSWORD='senha-local' \
./tools/visual-qa/capture-screenshots.sh
```

No PowerShell, defina as mesmas variáveis com `$env:NOME='valor'` e execute `./tools/visual-qa/capture-screenshots.ps1`.

O aplicativo deve estar em execução, com banco migrado e seed local. Credenciais nunca são armazenadas. Como alternativa, faça login manual em um navegador Playwright, exporte seu `storageState` e informe o caminho em `VISUAL_QA_STORAGE_STATE`. Use `VISUAL_QA_HEADLESS=false` para acompanhar a captura. Uma sessão ausente redireciona ao login e é reportada como falha, em vez de produzir uma evidência enganosa.

O Playwright é uma dependência estritamente opcional desta ferramenta: não foi adicionado ao build .NET nem ao restore da solução.
