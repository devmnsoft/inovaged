# Actionlint: validação local e causa estrutural

## Instalação

### Linux

Instale uma versão fixada do binário publicada pelo projeto actionlint, valide seu checksum e disponibilize `actionlint` no `PATH`. Alternativamente, com Go disponível:

```bash
GOBIN="$HOME/.local/bin" go install github.com/rhysd/actionlint/cmd/actionlint@latest
```

### Windows

Use `scoop install actionlint` ou baixe a versão fixada para Windows, valide o checksum e inclua o diretório no `PATH`.

### CI

O workflow canônico usa `raven-actions/actionlint@v2` no primeiro job. O mesmo conjunto de arquivos pode ser validado antes do push com:

```bash
bash scripts/ci/lint-workflows.sh
```

## Correção

Somente `.github/workflows/inovaged-ci.yml` permanece executável. `ci.yml` e `dotnet-ci.yml` foram preservados como arquivos `.disabled` para auditoria. Guardas de movimentação, regex, pacotes, segredos, migrations, JSON, agente Windows e artefatos permanecem no gate canônico.

O log remoto original não estava acessível na imagem sem GitHub CLI/autenticação; por isso este relatório não atribui uma regra ou linha sem evidência. A captura autenticada indicada no diagnóstico deve ser anexada antes de declarar o gate remoto verde.
