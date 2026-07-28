# Proteção cross-tenant

Há três barreiras: joins de leitura exigem igualdade entre o tenant do usuário e da role; atribuições administrativas usam `INSERT ... SELECT` limitado pelo tenant; e `trg_user_role_same_tenant` rejeita INSERT/UPDATE inválido no PostgreSQL. A migration faz uma varredura prévia e não remove órfãos ou vínculos cruzados silenciosamente.
