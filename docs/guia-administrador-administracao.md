# Administração, Segurança e Governança

Esta evolução adiciona um hub administrativo incremental e preserva rotas, tabelas e fluxos existentes. As novas estruturas são aditivas e idempotentes, com modo de permissões LEGACY por padrão, AUDIT_ONLY para diagnóstico e ENFORCED para aplicação futura controlada.

## Rotas
- /Administration
- /Administration/Security
- /Administration/Identities
- /Administration/Users
- /Administration/Audit
- /Administration/Tenants
- /Administration/Workers
- /Administration/Health
- /Administration/Settings
- /Administration/Migrations
- /Administration/Compliance

## Segurança
Todos os endpoints usam AppPolicies.Administracao. CPFs completos e segredos não são exibidos; configurações sensíveis são mascaradas.

## Migração
Aplicar `database/migrations/2026_07_administration_security_governance.sql` após backup. Rollback lógico: definir `reg_status='I'` nas novas tabelas e retornar tenants para LEGACY.

## Pendências
Evoluções futuras podem ampliar edição auditada, migração em lote completa, MFA e enforcement global.
