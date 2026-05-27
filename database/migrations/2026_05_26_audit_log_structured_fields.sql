alter table ged.audit_log
add column if not exists event_type text null,
add column if not exists source text null,
add column if not exists details text null,
add column if not exists exception_type text null,
add column if not exists exception_message text null,
add column if not exists stack_trace text null,
add column if not exists path text null,
add column if not exists http_method text null,
add column if not exists http_status integer null,
add column if not exists ip_address text null,
add column if not exists user_agent text null,
add column if not exists elapsed_ms bigint null,
add column if not exists correlation_id text null,
add column if not exists data jsonb null;

do $$
begin
    if exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'audit_log'
          and column_name = 'created_at'
    ) then
        create index if not exists ix_audit_log_tenant_created
        on ged.audit_log(tenant_id, created_at desc);
    elsif exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'audit_log'
          and column_name = 'reg_date'
    ) then
        create index if not exists ix_audit_log_tenant_reg_date
        on ged.audit_log(tenant_id, reg_date desc);
    elsif exists (
        select 1
        from information_schema.columns
        where table_schema = 'ged'
          and table_name = 'audit_log'
          and column_name = 'event_time'
    ) then
        create index if not exists ix_audit_log_tenant_event_time
        on ged.audit_log(tenant_id, event_time desc);
    end if;
end $$;

create index if not exists ix_audit_log_event_type
on ged.audit_log(event_type);

create index if not exists ix_audit_log_source
on ged.audit_log(source);

create index if not exists ix_audit_log_correlation_id
on ged.audit_log(correlation_id);
