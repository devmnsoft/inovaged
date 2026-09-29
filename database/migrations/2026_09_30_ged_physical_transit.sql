-- Bloco B: trânsito físico de prontuários (DAME <-> setores hospitalares).
-- Aditiva e idempotente. Não altera o comportamento das caixas físicas (ged.box / empréstimos por caixa).
create schema if not exists ged;

-- Cabeçalho do lote de saída/devolução (um lote = N prontuários para UM setor destino, com UM responsável).
create table if not exists ged.physical_loan_batch (
 id uuid primary key default gen_random_uuid(),
 tenant_id uuid not null,
 batch_number text not null,
 destination_sector_id uuid null references ged.protocolo_setor(id),
 destination_sector_name text not null,
 carrier_name text null,
 carrier_id text null,
 reason text null,
 due_at timestamptz null,
 items_total integer not null default 0,
 items_returned integer not null default 0,
 status varchar(40) not null default 'OPEN',
 created_by uuid null,
 created_by_name text null,
 created_at timestamptz not null default now(),
 closed_at timestamptz null,
 closed_by uuid null,
 closed_by_name text null,
 notes text null,
 correlation_id text null,
 reg_status char(1) not null default 'A'
);
create unique index if not exists ux_physical_loan_batch_number on ged.physical_loan_batch(tenant_id,batch_number) where reg_status='A';
create index if not exists ix_physical_loan_batch_status on ged.physical_loan_batch(tenant_id,status,created_at desc) where reg_status='A';
create index if not exists ix_physical_loan_batch_sector on ged.physical_loan_batch(tenant_id,destination_sector_id,status) where reg_status='A';

-- Linhas de empréstimo por prontuário reutilizam ged.physical_loan (coluna document_id já existia, inativa antes).
alter table ged.physical_loan add column if not exists batch_id uuid null references ged.physical_loan_batch(id);
alter table ged.physical_loan add column if not exists sector_id uuid null references ged.protocolo_setor(id);
alter table ged.physical_loan add column if not exists sector_name text null;
alter table ged.physical_loan add column if not exists carrier_name text null;
alter table ged.physical_loan add column if not exists carrier_id text null;
alter table ged.physical_loan add column if not exists lost_reason text null;
create index if not exists ix_physical_loan_batch on ged.physical_loan(tenant_id,batch_id) where reg_status='A';
create index if not exists ix_physical_loan_sector on ged.physical_loan(tenant_id,sector_id,status) where reg_status='A';

-- RN01: nenhum segundo empréstimo aberto para o mesmo prontuário/tenant (fallback de banco para o guarda-serviço).
create unique index if not exists ux_physical_loan_open_document on ged.physical_loan(tenant_id,document_id) where reg_status='A' and status='OPEN' and document_id is not null;

-- B1: setores DAME ausentes, seed idempotente por (tenant, nome) para todos os tenants existentes.
insert into ged.protocolo_setor(tenant_id,nome,sigla,descricao,ativo,ordem,created_at,reg_status)
select t.id, v.nome, v.sigla, 'Setor hospitalar (seed inicial do Bloco B).', true,
       (select coalesce(max(p.ordem),0)+1 from ged.protocolo_setor p where p.tenant_id=t.id and p.reg_status='A')
       + (row_number() over (partition by t.id order by v.nome) - 1),
       now(), 'A'
from ged.tenant t
cross join (values ('UAI','UAI'),('Ambulatório','AMB'),('Radioterapia','RTQ'),('Quimioterapia','QT'),('Admissão de Pacientes','ADM')) as v(nome,sigla)
where not exists (select 1 from ged.protocolo_setor y where y.tenant_id=t.id and y.reg_status='A' and upper(btrim(y.nome)) = upper(btrim(v.nome)));
