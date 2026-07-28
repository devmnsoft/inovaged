# Modelo canônico de identidade

A autenticação usa exclusivamente `ged.tenant`, `ged.app_user`, `ged.app_role` e a relação binária `ged.user_role(user_id, role_id)`. O tenant efetivo vem do usuário; uma role somente é efetiva quando `app_role.tenant_id = app_user.tenant_id`. Permissões são identificadas por código por meio de `role_permission.permission_code = permission.code`. As tabelas plurais permanecem legadas e não são fallback do login.
