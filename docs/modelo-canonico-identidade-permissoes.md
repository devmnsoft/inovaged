# Modelo canônico de identidade e permissões

| Estrutura | Chave | Tenant | Relacionamentos | Legado/equivalente | Uso nesta evolução |
| --- | --- | --- | --- | --- | --- |
| `ged.tenant` | `id` | própria linha | `ged.app_user.tenant_id` | `tenants` plural não canônico | Métrica de tenants ativos. |
| `ged.app_user` | `id` | `tenant_id` | `servidor_id`, `ged.user_role.user_id` | `users` plural não canônico | Login, usuários ativos/bloqueados e autorização. |
| `ged.servidor` | `id` | `tenant_id` | Origem preferencial de CPF | origens legadas em usuário | Consulta de login preserva CPF do servidor. |
| `ged.app_role` | `id` | `tenant_id` | `ged.user_role.role_id`, `ged.role_permission.role_id` | `roles` plural não canônico | Métrica de roles e autorização. |
| `ged.user_role` | `user_id`, `role_id` | `tenant_id` | Vínculo usuário-role | `ged.user_roles` em fluxos legados | Login e autorização filtram tenant. |
| `ged.permission` | `id`/`code` | `tenant_id` | `ged.role_permission.permission_id` | `permissions` plural não canônico | Métrica e catálogo de permissões. |
| `ged.role_permission` | `role_id`, `permission_id` | `tenant_id` | Concede permissão ativa | N/A | `DatabasePermissionChecker`. |

Nenhuma tabela plural foi removida. Ambientes que ainda escrevem em plural devem criar views/adapters de compatibilidade antes de ativar `ENFORCED`.
