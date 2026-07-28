# Evolução 04.1.9 — homologação operacional CMS

Este documento registra a evolução incremental do release candidate CMS destacado, preservando documentos, versões, assinaturas anteriores e migrations existentes.

## Escopo implementado

- Conclusão CMS passa a abrir a unidade de trabalho antes do lock, da consulta de idempotência e do consumo do token.
- O lock transacional usa `SELECT ... FOR UPDATE` em `ged.signing_session`.
- O token de conclusão, assinatura, validation run, checks, cadeia, evidência, evento e conclusão da sessão são gravados na mesma conexão/transação.
- Rollback executa antes do registro seguro de falha em transação curta separada.
- `certificate_status` e `trust_status` são adicionados de forma aditiva e recebem backfill `NOT_VERIFIABLE`.
- `TrustStatus` calculado pelo factory é mantido no resultado e persistido com a assinatura e a execução de validação.
- O job `cms-e2e` executa projeto dedicado sem filtro, falha em `total=0`, gera fixture CMS sintética e valida com `openssl cms -verify`.
- Pairing direto por `/pair` foi descontinuado em favor de challenge com código, aprovação local, tentativas, expiração e uso único.
- A interface `Signature/Cryptographic.cshtml` separa registro interno operacional de assinatura CMS e inclui antiforgery para chamadas do navegador.

## Gate inicial

- SHA inicial registrado: `d90c3de436e0de298a77bcf142b526fd370ca758`.
- Branch criada: `codex/cms-rc3-homologacao-operacional-real`.
- `dotnet` não está instalado no container local, portanto restore/build/test/migrations/hosts não puderam ser executados localmente.
- `gh` não está instalado no container local, portanto workflows reconhecidos, permissões de Actions e branch protection precisam ser confirmados fora deste ambiente.
- Configuração externa necessária: proteger `main` exigindo `actionlint`, `server-linux`, `agent-windows`, `security-guards` e `cms-e2e`.

## Limitações desta etapa

- Não declara ICP-Brasil, assinatura qualificada, AD-RB/CAdES DOC-ICP-15, revogação, carimbo do tempo confiável ou PAdES.
- `conformity_status` permanece `NOT_EVALUATED` e `COMPLIANT` não é gerado automaticamente.
- A materialização CMS continua limitada pelo `SignedCms`; o agente documenta e configura `SigningAgent:MaxCmsMaterializationSizeMb`.

## Rollback operacional

Aplicar rollback por reversão do deploy da aplicação e, se necessário, manter a migration aditiva sem remover colunas, pois ela é compatível com dados existentes e usa defaults seguros.
