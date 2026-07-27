# Actionlint — análise de causa raiz

## Run solicitado

- Workflow: `inovaged-ci`
- Run: `30210987347`
- Job: `89817049106`

## Resultado da investigação

Não foi possível recuperar a mensagem exata. O executável `gh` não está instalado (exit 127), o checkout não possui remote ou credenciais e o acesso HTTP externo foi recusado (HTTP 403). Registrar arquivo, linha, regra ou causa sem o log violaria o requisito de não assumir a causa.

## Correções preventivas verificáveis no patch

O workflow canônico passou a nomear steps, fixar timeout de 30 minutos, aplicar concurrency, encadear as fases e chamar a validação reutilizável de TRX. A invocação `raven-actions/actionlint@v2` permanece sem desabilitar ShellCheck, Pyflakes, schema ou expressões. A confirmação da causa raiz continua pendente do log original e deve ser preenchida no run canônico antes de promover a PR.
