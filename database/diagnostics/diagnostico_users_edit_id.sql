-- Diagnóstico de ID recebido no GET /Users/Edit/{id}
-- Parâmetros esperados: @TenantId (uuid), @Id (uuid)

-- 1) Verificar o ID recebido como servidor
select *
from ged.servidor s
where s.tenant_id = @TenantId
  and s.id = @Id;

-- 2) Verificar o ID recebido como usuário de acesso
select *
from ged.app_user u
where u.tenant_id = @TenantId
  and u.id = @Id;

-- 3) Verificar vínculo servidor-usuário para o mesmo ID
select
    s.id as servidor_id,
    u.id as user_id,
    s.nome_completo,
    s.cpf,
    u.email
from ged.servidor s
left join ged.app_user u
       on u.servidor_id = s.id
      and u.tenant_id = s.tenant_id
      and u.deleted_at_utc is null
where s.tenant_id = @TenantId
  and (s.id = @Id or u.id = @Id);
