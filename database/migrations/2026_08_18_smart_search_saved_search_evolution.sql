-- Evolução idempotente das buscas salvas do SmartSearch.
begin;
alter table if exists ged.smart_search_saved_search add column if not exists last_run_at timestamptz null;
alter table if exists ged.smart_search_saved_search add column if not exists run_count integer not null default 0;
alter table if exists ged.smart_search_saved_search add column if not exists is_favorite boolean not null default false;
alter table if exists ged.smart_search_saved_search add column if not exists updated_at timestamptz not null default now();
alter table if exists ged.smart_search_saved_search add column if not exists reg_status char(1) not null default 'A';
create index if not exists ix_smart_search_saved_search_favorites
    on ged.smart_search_saved_search(tenant_id,user_id,is_favorite desc,created_at desc)
    where coalesce(reg_status,'A')='A';
commit;
