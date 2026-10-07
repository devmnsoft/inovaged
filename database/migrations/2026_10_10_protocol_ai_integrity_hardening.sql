-- Hardening incremental da assistencia de IA do Protocolo.
-- Preserva historico existente e impede novas decisoes duplicadas por escopo.

create table if not exists ged.protocolo_ai_revisao_duplicate_preflight (
    tenant_id uuid not null,
    execution_id uuid not null,
    task varchar(64) not null,
    duplicate_count integer not null,
    first_created_at timestamptz null,
    last_created_at timestamptz null,
    detected_at timestamptz not null default now(),
    primary key (tenant_id, execution_id, task, detected_at)
);

insert into ged.protocolo_ai_revisao_duplicate_preflight (
    tenant_id, execution_id, task, duplicate_count, first_created_at, last_created_at
)
select tenant_id, execution_id, task, count(*)::int, min(created_at), max(created_at)
from ged.protocolo_ai_revisao
group by tenant_id, execution_id, task
having count(*) > 1
on conflict do nothing;

create index if not exists ix_protocolo_ai_revisao_identity
    on ged.protocolo_ai_revisao(tenant_id, execution_id, task);

create or replace function ged.prevent_protocol_ai_revision_duplicate()
returns trigger
language plpgsql
as $$
begin
    if exists (
        select 1
        from ged.protocolo_ai_revisao r
        where r.tenant_id = new.tenant_id
          and r.execution_id = new.execution_id
          and r.task = new.task
    ) then
        raise exception 'PROTOCOLO_AI_REVISION_CONFLICT'
            using errcode = '23505';
    end if;

    return new;
end;
$$;

drop trigger if exists trg_prevent_protocol_ai_revision_duplicate on ged.protocolo_ai_revisao;
create trigger trg_prevent_protocol_ai_revision_duplicate
before insert on ged.protocolo_ai_revisao
for each row execute function ged.prevent_protocol_ai_revision_duplicate();
