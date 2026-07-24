# Release Gate InovaGED 04.1.10

Este documento registra o estado verificável do gate. Itens sem teste e evidência real permanecem como parciais, não atendidos ou bloqueados; não são declarados como concluídos.

## Escopo preservado

Documentos, versões, storage, uploads, OCR, preview, classificação, PCD, TTD, POP, retenção, protocolo, workflow, empréstimos, lotes, caixas, Guardião Documental, PACS, relatórios, usuários, permissões, auditoria, assinatura interna, assinaturas CMS, backups, portabilidade, migrations, isolamento multi-tenant e publicação IIS devem ser preservados.

## Requisitos externos

- Habilitar proteção da branch `main` no GitHub exigindo branch atualizada, aprovação, conversas resolvidas, bloqueio de force-push/delete e todos os checks do workflow `inovaged-ci` sem skipped.
- Executar o workflow no GitHub antes de retirar o draft.
- Executar migrations e testes E2E contra PostgreSQL real.

## Limitações formais

PAdES, OCSP, LCR e timestamp RFC 3161 permanecem fora desta evolução e devem aguardar o gate integralmente verde.
