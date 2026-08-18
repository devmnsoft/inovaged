-- Harden the persisted read paths used by Dapper and the conversational history.
begin;

update ged.smart_search_saved_search set run_count = 0 where run_count < 0;
alter table ged.smart_search_saved_search
    drop constraint if exists ck_smart_search_saved_search_run_count;
alter table ged.smart_search_saved_search
    add constraint ck_smart_search_saved_search_run_count check (run_count >= 0);

create index if not exists ix_smart_search_message_history
    on ged.smart_search_message(tenant_id, conversation_id, created_at, id)
    where reg_status = 'A';

create index if not exists ix_smart_search_conversation_owner
    on ged.smart_search_conversation(tenant_id, user_id, id)
    where reg_status = 'A';

commit;
