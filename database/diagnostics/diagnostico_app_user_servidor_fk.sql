-- app_user sem servidor_id
select
    u.id as user_id,
    u.tenant_id,
    u.name,
    u.email,
    u.servidor_id
from ged.app_user u
where u.deleted_at_utc is null
and (
    u.servidor_id is null
    or u.servidor_id = '00000000-0000-0000-0000-000000000000'::uuid
);

-- app_user com servidor_id inexistente
select
    u.id as user_id,
    u.tenant_id,
    u.name,
    u.email,
    u.servidor_id
from ged.app_user u
left join ged.servidor s
       on s.id = u.servidor_id
      and s.tenant_id = u.tenant_id
where u.deleted_at_utc is null
and u.servidor_id is not null
and u.servidor_id <> '00000000-0000-0000-0000-000000000000'::uuid
and s.id is null;

-- usuários listados na view com vínculo inconsistente
select
    v.tenant_id,
    v.servidor_id,
    v.user_id,
    v.nome_completo,
    v.email,
    s.id as servidor_exists
from ged.vw_user_admin_list v
left join ged.servidor s
       on s.id = v.servidor_id
      and s.tenant_id = v.tenant_id
where v.user_id is not null
and (
    v.servidor_id is null
    or v.servidor_id = '00000000-0000-0000-0000-000000000000'::uuid
    or s.id is null
);
