# Evolução 04.1.7

Documento da Evolução 04.1.7.

## Estado implementado nesta alteração

Esta alteração consolida o contrato tipado do fluxo CMS, move a criação da sessão para o caso de uso/orquestrador, documenta o gate inicial e adiciona a base de unidade de trabalho transacional PostgreSQL.

## Critérios ainda pendentes de homologação real

A execução completa de build, testes, migrations, OpenSSL e workflows obrigatórios depende de ambiente com SDK .NET, GitHub CLI, PostgreSQL e runners Linux/Windows. A PR deve permanecer draft até os checks `actionlint`, `server-linux`, `agent-windows` e `security-guards` passarem.

## Regras de veracidade

Para CMS matematicamente válido sem política, revogação e timestamp avaliados, manter `CMS_DETACHED`, `CMS_PKCS7_DETACHED`, `UNKNOWN`, `LOCAL_AGENT`, `VALID`, `INDETERMINATE` e `NOT_EVALUATED`. Não persistir nem exibir conformidade `COMPLIANT` nesta evolução.
