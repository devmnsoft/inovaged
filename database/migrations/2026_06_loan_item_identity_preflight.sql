-- Upgrade the historical composite key before the published manual-item migration
-- makes document_id nullable. No rows are removed and the old tuple stays unique.
do $$
declare pk text; columns text[];
begin
    if to_regclass('ged.loan_request_item') is null then return; end if;
    select c.conname, array_agg(a.attname::text order by k.ordinality)
      into pk, columns
    from pg_constraint c
    cross join lateral unnest(c.conkey) with ordinality k(attnum, ordinality)
    join pg_attribute a on a.attrelid=c.conrelid and a.attnum=k.attnum
    where c.conrelid='ged.loan_request_item'::regclass and c.contype='p'
    group by c.conname;
    if columns = array['tenant_id','loan_id','document_id'] then
        alter table ged.loan_request_item add column if not exists id uuid default gen_random_uuid();
        update ged.loan_request_item set id=gen_random_uuid() where id is null;
        alter table ged.loan_request_item alter column id set not null;
        create unique index if not exists ux_loan_item_legacy_identity on ged.loan_request_item(tenant_id,loan_id,document_id);
        -- Deliberately no CASCADE: unexpected external FKs require an explicit migration.
        execute format('alter table ged.loan_request_item drop constraint %I', pk);
        alter table ged.loan_request_item add constraint pk_loan_request_item primary key(id);
    end if;
end $$;
