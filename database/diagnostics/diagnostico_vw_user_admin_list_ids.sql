-- Diagnóstico de IDs inválidos/ausentes na view administrativa.
select
    tenant_id,
    servidor_id,
    user_id,
    nome_completo,
    email,
    cpf,
    matricula,
    case
        when servidor_id is null then 'SERVIDOR_NULL'
        when servidor_id = '00000000-0000-0000-0000-000000000000'::uuid then 'SERVIDOR_EMPTY'
        else 'SERVIDOR_OK'
    end as servidor_status,
    case
        when user_id is null then 'USER_NULL'
        when user_id = '00000000-0000-0000-0000-000000000000'::uuid then 'USER_EMPTY'
        else 'USER_OK'
    end as user_status
from ged.vw_user_admin_list
where tenant_id = '00000000-0000-0000-0000-000000000001'
and (
    servidor_id is null
    or servidor_id = '00000000-0000-0000-0000-000000000000'::uuid
    or user_id is null
    or user_id = '00000000-0000-0000-0000-000000000000'::uuid
)
order by nome_completo;

select
    count(*) total,
    count(*) filter (where servidor_id is null or servidor_id = '00000000-0000-0000-0000-000000000000'::uuid) servidor_invalido,
    count(*) filter (where user_id is null or user_id = '00000000-0000-0000-0000-000000000000'::uuid) user_invalido
from ged.vw_user_admin_list
where tenant_id = '00000000-0000-0000-0000-000000000001';

-- Diagnóstico específico de servidor_id listado pela view mas inexistente em ged.servidor.
-- Se retornar linhas, a view/dados estão inconsistentes e o botão Editar deve usar UserId
-- ou o fallback ADMIN deve reparar o servidor antes de abrir a edição.
select
    v.tenant_id,
    v.servidor_id,
    v.user_id,
    v.nome_completo,
    v.email,
    exists(select 1 from ged.servidor s where s.id = v.servidor_id and s.tenant_id = v.tenant_id) as servidor_exists,
    exists(select 1 from ged.app_user u where u.id = v.user_id and u.tenant_id = v.tenant_id) as user_exists
from ged.vw_user_admin_list v
where v.tenant_id = '00000000-0000-0000-0000-000000000001'
and (
    v.servidor_id is not null
    and not exists (
        select 1 from ged.servidor s
        where s.id = v.servidor_id
        and s.tenant_id = v.tenant_id
    )
)
order by v.nome_completo;
