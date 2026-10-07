-- Bind governed protocol AI executions to their origin, selected modality and source snapshot.
alter table ged.ai_execution
    add column if not exists context_metadata jsonb null;

alter table ged.ai_execution
    drop constraint if exists ck_ai_execution_context_metadata_object;

alter table ged.ai_execution
    add constraint ck_ai_execution_context_metadata_object
    check (context_metadata is null or jsonb_typeof(context_metadata) = 'object');

-- Collapse duplicate preflight snapshots while retaining the widest diagnostic bounds.
with ranked as (
    select ctid,
           tenant_id,
           execution_id,
           task,
           first_value(ctid) over (
               partition by tenant_id, execution_id, task
               order by detected_at, ctid
           ) as keep_ctid,
           max(duplicate_count) over (partition by tenant_id, execution_id, task) as duplicate_count,
           min(first_created_at) over (partition by tenant_id, execution_id, task) as first_created_at,
           max(last_created_at) over (partition by tenant_id, execution_id, task) as last_created_at
    from ged.protocolo_ai_revisao_duplicate_preflight
)
update ged.protocolo_ai_revisao_duplicate_preflight target
   set duplicate_count = ranked.duplicate_count,
       first_created_at = ranked.first_created_at,
       last_created_at = ranked.last_created_at
  from ranked
 where target.ctid = ranked.keep_ctid;

delete from ged.protocolo_ai_revisao_duplicate_preflight target
using ged.protocolo_ai_revisao_duplicate_preflight keeper
where target.tenant_id = keeper.tenant_id
  and target.execution_id = keeper.execution_id
  and target.task = keeper.task
  and target.detected_at > keeper.detected_at;

create unique index if not exists ux_protocol_ai_revision_preflight_identity
    on ged.protocolo_ai_revisao_duplicate_preflight(tenant_id, execution_id, task);

create table if not exists ged.protocolo_ai_revisao_identity (
    tenant_id uuid not null,
    execution_id uuid not null,
    task varchar(64) not null,
    primary key (tenant_id, execution_id, task)
);

insert into ged.protocolo_ai_revisao_identity (tenant_id, execution_id, task)
select distinct tenant_id, execution_id, task
from ged.protocolo_ai_revisao
on conflict do nothing;

insert into ged.protocolo_ai_revisao_duplicate_preflight (
    tenant_id, execution_id, task, duplicate_count, first_created_at, last_created_at
)
select tenant_id, execution_id, task, count(*)::int, min(created_at), max(created_at)
from ged.protocolo_ai_revisao
group by tenant_id, execution_id, task
having count(*) > 1
on conflict (tenant_id, execution_id, task) do update
set duplicate_count = greatest(
        ged.protocolo_ai_revisao_duplicate_preflight.duplicate_count,
        excluded.duplicate_count
    ),
    first_created_at = least(
        ged.protocolo_ai_revisao_duplicate_preflight.first_created_at,
        excluded.first_created_at
    ),
    last_created_at = greatest(
        ged.protocolo_ai_revisao_duplicate_preflight.last_created_at,
        excluded.last_created_at
    );

create or replace function ged.prevent_protocol_ai_revision_duplicate()
returns trigger
language plpgsql
as $$
declare
    identity_created boolean;
begin
    insert into ged.protocolo_ai_revisao_identity (tenant_id, execution_id, task)
    values (new.tenant_id, new.execution_id, new.task)
    on conflict do nothing
    returning true into identity_created;

    if not coalesce(identity_created, false) then
        raise exception 'PROTOCOLO_AI_REVISION_CONFLICT'
            using errcode = '23505';
    end if;

    return new;
end;
$$;
