create schema if not exists ged;
create extension if not exists pgcrypto;

create table if not exists ged.smart_assistant_session (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, user_id uuid null, title text null,
 status varchar(40) not null default 'ACTIVE', created_at timestamptz not null default now(), last_message_at timestamptz null, reg_status char(1) not null default 'A');
create table if not exists ged.smart_assistant_message (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, session_id uuid not null references ged.smart_assistant_session(id), user_id uuid null,
 role varchar(30) not null, content text not null, normalized_content text null, confidence numeric(5,2) not null default 0,
 answer_status varchar(40) not null default 'ANSWERED', created_at timestamptz not null default now(), payload_json jsonb null, reg_status char(1) not null default 'A');
create table if not exists ged.smart_assistant_citation (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, message_id uuid not null references ged.smart_assistant_message(id), source_type varchar(80) not null,
 source_id uuid null, source_title text null, source_excerpt text null, source_url text null, confidence numeric(5,2) not null default 0, created_at timestamptz not null default now(), reg_status char(1) not null default 'A');
create table if not exists ged.smart_assistant_action_suggestion (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, message_id uuid not null references ged.smart_assistant_message(id), action_type varchar(80) not null,
 title text not null, description text null, target_type varchar(80) null, target_id uuid null, status varchar(40) not null default 'PENDING', created_at timestamptz not null default now(),
 reviewed_by uuid null, reviewed_at timestamptz null, review_notes text null, reg_status char(1) not null default 'A');
create index if not exists ix_smart_assistant_session_tenant on ged.smart_assistant_session(tenant_id, created_at desc);
create index if not exists ix_smart_assistant_message_session on ged.smart_assistant_message(session_id, created_at);
create index if not exists ix_smart_assistant_citation_message on ged.smart_assistant_citation(message_id);
create index if not exists ix_smart_assistant_action_status on ged.smart_assistant_action_suggestion(tenant_id, status, created_at desc);
