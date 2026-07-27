# Diagnóstico da evolução 04.1.12

## Identificação

- Run solicitado: `30271402872` (`inovaged-ci`).
- Job solicitado: `89994573754` (`actionlint`).
- SHA inicial local: `9a396b3391b22cb6993710ac822acaf815e581d7`.
- Branch de trabalho: `codex/release-gate-verde-cms-poc-executavel`.

## Evidência do run remoto

A imagem de execução não contém o GitHub CLI (`gh: command not found`). A tentativa de obter o log público pela API de jobs retornou HTTP 403, porque downloads de logs exigem autenticação. Portanto, a mensagem remota exata, arquivo, linha e regra **não são inventados neste documento**. O comando de captura obrigatório deve ser executado em uma estação autenticada:

```bash
mkdir -p artifacts/ci
gh run view 30271402872 --json name,workflowName,status,conclusion,event,headSha,url
gh run view 30271402872 --job 89994573754 --log | tee artifacts/ci/actionlint-run-9.log
```

## Causa estrutural corrigida

O action `raven-actions/actionlint` examina todo `.github/workflows`. Havia três workflows executáveis, inclusive dois legados com shell e Python embutidos. Assim, um erro em qualquer legado bloqueava o primeiro gate e todos os jobs dependentes. As guardas úteis foram incorporadas ao workflow canônico e os legados foram arquivados, sem extensão `.yml`, em `docs/ci/archive`.

A validação final local fica centralizada em `scripts/ci/lint-workflows.sh`, sem desativar ShellCheck/Pyflakes, sem `continue-on-error` e sem tolerância artificial de falhas.
