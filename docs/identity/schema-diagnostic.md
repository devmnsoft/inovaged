# Diagnóstico do schema de identidade

## Evidência disponível

O ambiente de execução de 2026-07-28 não expôs uma connection string PostgreSQL, `psql` nem `docker`; portanto as consultas de `information_schema` e `pg_constraint` solicitadas **não foram executadas contra um banco vivo**. Não há resultados inventados neste documento. O diagnóstico reproduzível no CI foi adicionado com PostgreSQL 16 e a fixture legada.

O dump versionado `gedscript.sql` contém `ged.app_role(id, tenant_id, name, normalized_name, created_at)`, `ged.app_user` (incluindo `id`, `tenant_id`, `is_active` e `deleted_at_utc`) e `ged.user_role(user_id, role_id)`. Também contém o modelo canônico `role_permission(tenant_id, role_id, permission_code, reg_status)` e `permission(code, name)`, além das estruturas plurais legadas.

## Divergência que causou o incidente

O código consultava `user_role.tenant_id`, `user_role.is_active` e `app_role.is_active`, ausentes na relação registrada no dump e na fixture. A associação canônica deriva o tenant por `app_user.tenant_id` e exige igualdade com `app_role.tenant_id`.

## Constraints e índices

A migration desta entrega verifica vínculos órfãos/cross-tenant antes de qualquer endurecimento, cria FKs ausentes, índices por coluna, unicidade `(user_id, role_id)`, trigger de isolamento e view efetiva. Ela interrompe sem apagar dados quando encontra inconsistências.

## Consulta para execução operacional

Antes de promover, a equipe de ambiente deve executar as duas consultas de catálogo descritas na solicitação, a consulta direta de roles e anexar a saída sanitizada à PR. Essa pendência mantém a entrega em draft.
