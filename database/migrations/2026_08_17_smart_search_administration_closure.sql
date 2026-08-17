-- Fechamento SmartSearch + Administração. Exclusivamente aditivo e idempotente.
begin;
create schema if not exists ged;

create table if not exists ged.smart_search_saved_search (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, user_id uuid not null,
 name varchar(120) not null, query_text varchar(500) not null,
 query_hash text generated always as (md5(query_text)) stored,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create unique index if not exists ux_smart_search_saved_search_active
 on ged.smart_search_saved_search(tenant_id,user_id,query_hash) where reg_status='A';
create index if not exists ix_smart_search_saved_search_user
 on ged.smart_search_saved_search(tenant_id,user_id,updated_at desc) where reg_status='A';

create table if not exists ged.smart_search_intent (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, code varchar(80) not null,
 display_name varchar(160) not null, patterns jsonb not null default '[]', filter_definition jsonb not null default '{}',
 priority int not null default 100, is_sensitive boolean not null default false,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), reg_status char(1) not null default 'A'
);
create unique index if not exists ux_smart_search_intent_code on ged.smart_search_intent(tenant_id,code);

create table if not exists ged.smart_search_metric (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, metric_date date not null,
 metric_name varchar(100) not null, metric_value numeric(18,4) not null default 0, dimensions jsonb not null default '{}',
 created_at timestamptz not null default now()
);
create unique index if not exists ux_smart_search_metric_daily on ged.smart_search_metric(tenant_id,metric_date,metric_name);

create table if not exists ged.smart_search_query_audit (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, user_id uuid not null,
 query_hash varchar(64) not null, intent_code varchar(80) null, result_count int not null default 0,
 sensitive boolean not null default false, correlation_id varchar(100) null, created_at timestamptz not null default now()
);
create index if not exists ix_smart_search_query_audit_tenant on ged.smart_search_query_audit(tenant_id,created_at desc);

-- Compatibilidade para ambientes que receberam versões iniciais das tabelas conversacionais.
alter table if exists ged.smart_search_conversation add column if not exists reg_status char(1) not null default 'A';
alter table if exists ged.smart_search_message add column if not exists sources_json jsonb not null default '[]';
alter table if exists ged.smart_search_message add column if not exists reg_status char(1) not null default 'A';

-- Catálogo mínimo por tenant, sem sobrescrever customizações existentes.
insert into ged.smart_search_intent(tenant_id,code,display_name,patterns,priority,is_sensitive)
select t.tenant_id, seed.code, seed.name, seed.patterns::jsonb, seed.priority, seed.sensitive
from (select distinct tenant_id from ged.document where tenant_id is not null) t
cross join (values
 ('WITHOUT_OCR','Documentos sem OCR','["sem OCR"]',10,false),
 ('WITHOUT_INDEX','Documentos sem índice','["sem índice","nao indexado"]',10,false),
 ('SUPPLIER','Fornecedor e CNPJ','["fornecedor","CNPJ"]',20,false),
 ('COMPETENCE','Competência','["competência"]',20,false),
 ('DUE_DATE','Vencimento','["vencimento","vence"]',20,false),
 ('AMOUNT','Valor documental','["valor","acima de R$"]',20,false),
 ('PROTOCOL','Protocolo e tramitação','["protocolo","tramitação"]',20,false),
 ('CLASSIFICATION','Classificação e temporalidade','["classificação","temporalidade"]',20,false),
 ('PHYSICAL_BOX','Caixa física','["caixa física","sem localização"]',20,false),
 ('HOSPITAL_BILLING','Faturamento hospitalar e glosa','["faturamento hospitalar","glosa","convênio"]',10,true),
 ('LOW_CONFIDENCE','Baixa confiança e revisão','["baixa confiança","pendente de revisão"]',10,false)
) seed(code,name,patterns,priority,sensitive)
on conflict(tenant_id,code) do nothing;
commit;
