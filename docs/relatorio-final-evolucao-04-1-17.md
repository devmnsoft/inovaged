# Relatório parcial — Hotfix 04.1.16-R2 / evolução 04.1.17

## Identificação

* SHA inicial: `a5ae6d408280681502ac04ebe4d8409f15c90cd2`.
* Branch: `codex/fix-login-role-schema-and-iam-evolution`.
* Causa: SQL de autenticação tratava a relação binária `user_role` como entidade tenant-aware e consultava colunas inexistentes.

## Entregue

A consulta de roles e o permission checker foram alinhados ao modelo canônico; a concessão automática de Operador e o uso de username como role foram removidos; sucesso ocorre após carregar autorização e criar cookie. A migration idempotente inclui pré-validação não destrutiva, FKs, índices, unicidade, trigger cross-tenant e view. A atribuição administrativa passou a validar tenant. Fixture legada, asserções PostgreSQL, testes de contrato e gate dedicado foram adicionados.

## Não concluído / riscos restantes

Não houve acesso ao banco real nem execução local de build/teste pela ausência das ferramentas. Não foram implementados nesta entrega: `IAccessDecisionService`, tenant por host, permission/security version, cache/invalidação, health check completo, Central de Acessos, matriz, sessões, simulador e suíte HTTP abrangente. O seed existente já usa a relação binária, mas sua revisão funcional completa permanece pendente. Os logs remotos anteriores e o novo CI precisam ser verificados antes de retirar o draft.

## Rollback operacional

Reverter o commit restaura o código. No banco, remover somente `trg_user_role_same_tenant`, `ged.enforce_user_role_same_tenant()`, `ged.vw_user_role_effective` e os índices/FKs nomeados pela migration, após avaliação administrativa. A migration não altera usuários, roles ou vínculos existentes.

## Gate de promoção

Manter draft até: diagnóstico no banco real anexado; builds Infrastructure/Web/solution; testes unitários e PostgreSQL; login HTTP; actionlint; `identity-auth-integration`; e `engineering-gate` verdes. Itens funcionais explicitamente pendentes exigem commits posteriores antes de afirmar atendimento integral dos critérios 10, 15–19 e 25.
