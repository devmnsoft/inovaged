create schema if not exists ged;
create extension if not exists pgcrypto;
create table if not exists ged.system_incident (
 id uuid primary key default gen_random_uuid(), tenant_id uuid null, correlation_id text null,
 incident_type varchar(80) not null, severity varchar(30) not null default 'MEDIUM', status varchar(30) not null default 'OPEN', signature_hash text not null,
 title text not null, message text not null, technical_message text null, recommended_action text null, recommended_script text null,
 controller text null, action text null, path text null, http_method varchar(20) null, sql_state varchar(20) null, database_object text null,
 exception_type text null, stack_trace text null, occurrence_count integer not null default 1, first_seen_at timestamptz not null default now(), last_seen_at timestamptz not null default now(),
 resolved_by uuid null, resolved_at timestamptz null, resolution_notes text null, payload_json jsonb null, reg_status char(1) not null default 'A');
create table if not exists ged.system_incident_event (
 id uuid primary key default gen_random_uuid(), incident_id uuid not null references ged.system_incident(id), tenant_id uuid null, correlation_id text null,
 event_type varchar(80) not null, event_message text not null, occurred_at timestamptz not null default now(), user_id uuid null, user_name text null,
 ip_address inet null, user_agent text null, payload_json jsonb null, reg_status char(1) not null default 'A');
create table if not exists ged.route_health_snapshot (
 id uuid primary key default gen_random_uuid(), route_path text not null, http_method varchar(20) not null default 'GET', expected_status text null,
 actual_status integer null, success boolean not null default false, duration_ms integer null, checked_at timestamptz not null default now(), error_message text null,
 correlation_id text null, payload_json jsonb null, reg_status char(1) not null default 'A');
create index if not exists ix_system_incident_status on ged.system_incident(status,severity,last_seen_at desc);
create index if not exists ix_system_incident_signature on ged.system_incident(signature_hash);
create index if not exists ix_system_incident_route on ged.system_incident(controller,action,path);
create index if not exists ix_system_incident_correlation on ged.system_incident(correlation_id);
create index if not exists ix_system_incident_event_incident on ged.system_incident_event(incident_id,occurred_at desc);
create index if not exists ix_route_health_snapshot_route on ged.route_health_snapshot(route_path,checked_at desc);
