# Atualização de baselines visuais

1. Inicie a aplicação e configure `INOVAGED_UI_BASE_URL` e `INOVAGED_UI_PASSWORD`.
2. Revise visualmente os PNGs em `Screenshots/actual`.
3. Fora do CI, execute a suíte com `INOVAGED_UPDATE_UI_BASELINES=true`.
4. Revise o diff dos arquivos em `Screenshots/golden` e submeta-os à aprovação humana.

O modo de atualização cria o diretório necessário e grava os bytes atuais. Ele registra a ação na saída, mas isso não significa aprovação. Se `CI=true`, a combinação com a variável de atualização lança uma exceção: pipelines nunca podem aprovar ou alterar baselines automaticamente.
