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
