# Homologação — InovaGED Guardião

## Matriz mínima de testes
1. Aplicar `database/migrations/2026_07_document_guardian.sql` duas vezes e confirmar idempotência.
2. Acessar `/DocumentGuardian/{documentId}` com usuário autorizado e documento do tenant.
3. Confirmar 404 para documento inexistente/fora do tenant.
4. Inserir finding com evidência e validar exibição.
5. Confirmar registro `DOCUMENT_GUARDIAN_VIEW` em auditoria.
6. Confirmar ausência de senha real em `appsettings.json`.
7. Confirmar `SystemSeed:Enabled=false` por padrão.
8. Confirmar bloqueio de `AllowInternalSelfSignedCertificates=true` em Production.

## Riscos restantes
Workers multi-tenant completos, pipeline PACS com quarentena/dead-letter, outbox legal e testes integrados com PostgreSQL devem ser finalizados em ciclos incrementais.
