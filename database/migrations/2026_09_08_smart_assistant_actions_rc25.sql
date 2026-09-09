begin;

alter table if exists ged.smart_assistant_action_suggestion
    add column if not exists target_url text null;

do $$
begin
    if to_regclass('ged.smart_assistant_action_suggestion') is not null then
        if not exists (
            select 1 from pg_constraint
            where conrelid='ged.smart_assistant_action_suggestion'::regclass
              and conname='ck_smart_assistant_action_target_url_local'
        ) then
            alter table ged.smart_assistant_action_suggestion
                add constraint ck_smart_assistant_action_target_url_local
                check (target_url is null or (length(target_url)<=500 and target_url like '/%' and target_url not like '//%' and target_url !~ E'[\\r\\n]'));
        end if;
    end if;
end $$;

commit;
