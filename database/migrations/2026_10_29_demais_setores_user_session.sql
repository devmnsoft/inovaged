do $$
declare upgrading boolean;
begin
    select not exists(select 1 from information_schema.columns
      where table_schema='ged' and table_name='sector_open_session' and column_name='authorizing_user_id') into upgrading;
    alter table ged.sector_open_code add column if not exists authorizing_user_id uuid;
    alter table ged.sector_open_session add column if not exists authorizing_user_id uuid;
    -- Earlier administrator openings cannot be distinguished from backend grants.
    -- Invalidate existing temporary grants once, preserving their history.
    if upgrading then
        update ged.sector_open_session set revoked_at=now() where revoked_at is null;
        update ged.sector_open_code set consumed_at=now() where consumed_at is null;
    end if;
end $$;

create index if not exists ix_sector_session_authorizing_user
on ged.sector_open_session(tenant_id, authorizing_user_id) where revoked_at is null;
