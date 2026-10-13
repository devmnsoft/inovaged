-- Demais setores: vínculos de paciente/atendimento, catálogo do Anexo I e conferência.
-- Não altera documentos clínicos de uma origem externa. O seed não atualiza linhas já personalizadas.

create table if not exists ged.sector_document_type (
    tenant_id uuid not null,
    catalog_code text not null,
    catalog_order integer not null,
    name text not null,
    applicability text not null,
    multiple_files boolean not null,
    physical_original boolean not null,
    view_sectors text[] not null,
    active boolean not null default true,
    customized boolean not null default false,
    synonyms text[] not null default '{}',
    mv_code text,
    ged_document_type_id uuid,
    primary key (tenant_id, catalog_code)
);

create table if not exists ged.sector_type_mapping (
    tenant_id uuid not null,
    catalog_code text not null,
    mapping_status text not null default 'PENDENTE',
    available_in_mv boolean,
    origin_name text,
    production_mode text,
    note text not null,
    customized boolean not null default false,
    primary key (tenant_id, catalog_code)
);

create table if not exists ged.sector_profile (
    id uuid primary key,
    tenant_id uuid not null,
    code text not null,
    version_no integer not null,
    name text not null,
    enabled boolean not null,
    customized boolean not null default false,
    published_at timestamptz not null default now(),
    unique (tenant_id, code, version_no)
);

create table if not exists ged.sector_profile_item (
    profile_id uuid not null references ged.sector_profile(id),
    catalog_code text not null,
    position integer not null,
    primary key (profile_id, catalog_code)
);

create table if not exists ged.sector_patient (
    id uuid primary key,
    tenant_id uuid not null,
    source_system text not null,
    source_unit text not null default '',
    external_code text not null,
    display_name text not null,
    updated_at timestamptz not null default now(),
    unique (tenant_id, source_system, source_unit, external_code)
);

create table if not exists ged.sector_encounter (
    id uuid primary key,
    tenant_id uuid not null,
    patient_id uuid not null references ged.sector_patient(id),
    external_code text not null,
    raw_kind text,
    kind text,
    admitted_at timestamptz,
    discharged_at timestamptz,
    status text,
    surgery boolean not null default false,
    opme boolean not null default false,
    high_cost boolean not null default false,
    updated_at timestamptz not null default now(),
    unique (tenant_id, patient_id, external_code)
);

create table if not exists ged.sector_kind_contract (
    tenant_id uuid not null,
    source_system text not null,
    raw_code text not null,
    kind text not null,
    primary key (tenant_id, source_system, raw_code)
);

create table if not exists ged.sector_access (
    tenant_id uuid not null,
    user_id uuid not null,
    sector_code text not null,
    permission_code text not null,
    reg_status char(1) not null default 'A',
    primary key (tenant_id, user_id, sector_code, permission_code)
);

create table if not exists ged.sector_type_scope (
    tenant_id uuid not null,
    user_id uuid not null,
    catalog_code text not null,
    primary key (tenant_id, user_id, catalog_code)
);

create table if not exists ged.sector_module_setting (
    tenant_id uuid primary key,
    folder_id uuid,
    updated_at timestamptz not null default now()
);

create table if not exists ged.sector_document_link (
    id uuid primary key,
    tenant_id uuid not null,
    patient_id uuid not null references ged.sector_patient(id),
    encounter_id uuid not null references ged.sector_encounter(id),
    document_id uuid not null,
    version_id uuid not null,
    catalog_code text not null,
    origin text not null,
    original_file_name text not null,
    content_sha256 char(64) not null,
    document_date date,
    sector_code text,
    notes text,
    issuer_name text,
    information_owner text,
    uploaded_by uuid not null,
    review_status text not null default 'RECEBIDO',
    physical_evidence text,
    transfer_status text not null,
    preview_status text not null,
    ocr_status text not null,
    extraction_status text not null,
    created_at timestamptz not null default now(),
    reg_status char(1) not null default 'A'
);

create index if not exists ix_sector_link_encounter on ged.sector_document_link (tenant_id, encounter_id) where reg_status = 'A';
create unique index if not exists ux_sector_link_version on ged.sector_document_link (tenant_id, version_id);

create table if not exists ged.sector_review_event (
    id uuid primary key,
    tenant_id uuid not null,
    link_id uuid not null references ged.sector_document_link(id),
    from_status text,
    to_status text not null,
    justification text,
    actor_id uuid not null,
    created_at timestamptz not null default now()
);

create table if not exists ged.sector_link_correction (
    id uuid primary key,
    tenant_id uuid not null,
    link_id uuid not null references ged.sector_document_link(id),
    from_encounter_id uuid not null,
    to_encounter_id uuid not null,
    reason text not null,
    actor_id uuid not null,
    created_at timestamptz not null default now()
);

create table if not exists ged.sector_extracted_field (
    id uuid primary key,
    tenant_id uuid not null,
    link_id uuid not null references ged.sector_document_link(id),
    version_id uuid not null,
    field_key text not null,
    context_code text not null,
    suggested_value text not null,
    confirmed_value text,
    evidence_excerpt text not null,
    page_number integer,
    status text not null,
    updated_by uuid,
    updated_at timestamptz not null default now()
);

create table if not exists ged.sector_field_review (
    id uuid primary key,
    tenant_id uuid not null,
    field_id uuid not null references ged.sector_extracted_field(id),
    from_status text,
    to_status text not null,
    from_value text,
    to_value text,
    actor_id uuid not null,
    created_at timestamptz not null default now()
);

create table if not exists ged.sector_finding (
    id uuid primary key,
    tenant_id uuid not null,
    encounter_id uuid not null,
    link_id uuid,
    description text not null,
    category text not null,
    evidence_excerpt text,
    page_number integer,
    review_status text not null default 'ABERTO',
    justification text,
    actor_id uuid,
    updated_at timestamptz not null default now()
);

create table if not exists ged.sector_delivery (
    id uuid primary key,
    tenant_id uuid not null,
    encounter_id uuid not null,
    profile_id uuid not null,
    status text not null,
    assembled_by uuid not null,
    recipient text,
    notes text,
    created_at timestamptz not null default now()
);

create table if not exists ged.sector_delivery_item (
    delivery_id uuid not null references ged.sector_delivery(id),
    link_id uuid not null,
    version_id uuid not null,
    outcome text not null,
    detail text,
    primary key (delivery_id, link_id)
);

create table if not exists ged.sector_embed_origin (
    id uuid primary key,
    tenant_id uuid not null,
    origin text not null,
    reg_status char(1) not null default 'A',
    unique (tenant_id, origin)
);

create table if not exists ged.sector_embed_grant (
    id uuid primary key,
    tenant_id uuid not null,
    version_id uuid not null,
    token_hash char(64) not null unique,
    expires_at timestamptz not null,
    revoked_at timestamptz,
    created_by uuid not null,
    created_at timestamptz not null default now()
);

create index if not exists ix_sector_patient_code on ged.sector_patient (tenant_id, external_code);

do $seed$
begin
    if to_regclass('ged.tenant') is null then
        return;
    end if;

    insert into ged.sector_document_type
        (tenant_id, catalog_code, catalog_order, name, applicability, multiple_files, physical_original, view_sectors)
    select t.id, v.code, v.ord, v.name, v.applicability, v.multiple, v.physical, v.views
    from ged.tenant t
    cross join (values
        (1, 'DS01', 'Ficha de Atendimento da Internação.', 'HOSPITAL_INTERNACAO', false, false, array['FATURAMENTO']::text[]),
        (2, 'DS02', 'Checklist de Organização do Prontuário Hospitalar.', 'HOSPITAL_INTERNACAO', false, false, array['FATURAMENTO']::text[]),
        (3, 'DS03', 'Sumário de Alta Hospitalar.', 'DISCHARGE', false, false, array['FATURAMENTO']::text[]),
        (4, 'DS04', 'Autorização de Procedimentos de Alto Custo — APAC.', 'HIGH_COST', false, false, array['FATURAMENTO']::text[]),
        (5, 'DS05', 'Visita Pré-Anestésica.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (6, 'DS06', 'Risco Cirúrgico — Cardiológico.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (7, 'DS07', 'Ficha de Anestesia Convencional — Boletim Anestésico.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (8, 'DS08', 'Descrição Cirúrgica.', 'SURGERY', true, false, array['FATURAMENTO','CME']::text[]),
        (9, 'DS09', 'Resultado de Exame Histopatológico.', 'SURGERY', true, false, array['FATURAMENTO']::text[]),
        (10, 'DS10', 'Registro de Cirurgia e Material Gasto na Sala.', 'SURGERY', true, false, array['FATURAMENTO','FARMACIA','CME']::text[]),
        (11, 'DS11', 'Controle de Material Usado em Cirurgia.', 'SURGERY', true, false, array['FATURAMENTO','FARMACIA','CME']::text[]),
        (12, 'DS12', 'Nota Fiscal da OPME.', 'OPME', true, true, array['FATURAMENTO','FARMACIA','CME']::text[]),
        (13, 'DS13', 'Documentos Pessoais do Paciente.', 'WHEN_APPLICABLE', true, true, array['FATURAMENTO']::text[]),
        (14, 'DS14', 'Termo de Responsabilidade assinado pelo paciente ou responsável.', 'WHEN_APPLICABLE', false, true, array['FATURAMENTO']::text[]),
        (15, 'DS15', 'Termo de Esclarecimento, Informação e Consentimento para Transporte e Análise de Amostra de Material Biológico.', 'WHEN_APPLICABLE', false, true, array['FATURAMENTO']::text[]),
        (16, 'DS16', 'Termo de Consentimento para Realização de Anestesia.', 'SURGERY', false, true, array['FATURAMENTO']::text[]),
        (17, 'DS17', 'Termo de Consentimento para Realização de Procedimento Cirúrgico.', 'SURGERY', false, true, array['FATURAMENTO']::text[]),
        (18, 'DS18', 'Documentação e Justificativas relativas à cobrança de diária de longa permanência, acompanhante, UTI, fisioterapia, entre outros.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (19, 'DS19', 'Laudo de Internação Hospitalar emitido pelo médico.', 'HOSPITAL_INTERNACAO', false, false, array['FATURAMENTO']::text[]),
        (20, 'DS20', 'Evolução Médica.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (21, 'DS21', 'Evolução de Enfermagem.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (22, 'DS22', 'Evolução Multiprofissional.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (23, 'DS23', 'Prescrição Médica.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO','FARMACIA']::text[]),
        (24, 'DS24', 'Prescrição de Enfermagem.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO','FARMACIA']::text[]),
        (25, 'DS25', 'Anotações de Enfermagem.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (26, 'DS26', 'Gráficos ou Folhas de Registro dos Sinais Vitais.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (27, 'DS27', 'Aviso de Cirurgia.', 'SURGERY', true, false, array['FATURAMENTO','CME']::text[]),
        (28, 'DS28', 'Admissão/Indução da Enfermagem.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (29, 'DS29', 'Sistematização da Assistência de Enfermagem no Intraoperatório.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (30, 'DS30', 'Sistematização da Assistência de Enfermagem — POI.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (31, 'DS31', 'Checklist de Cirurgia Segura.', 'SURGERY', false, false, array['FATURAMENTO']::text[]),
        (32, 'DS32', 'Formulários de Transferências de Cuidados.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[]),
        (33, 'DS33', 'Resultados de Exames Laboratoriais.', 'WHEN_APPLICABLE', true, false, array['FATURAMENTO']::text[])
    ) as v(ord, code, name, applicability, multiple, physical, views)
    where not exists (
        select 1 from ged.sector_document_type x where x.tenant_id = t.id and x.catalog_code = v.code
    );

    insert into ged.sector_type_mapping (tenant_id, catalog_code, mapping_status, note)
    select c.tenant_id, c.catalog_code, 'PENDENTE',
           case when c.catalog_code = 'DS19'
                then 'O anexo menciona avaliar a criação do Laudo de Internação no MV. Isso não confirma existência no MV nem exclusividade do GED.'
                else 'Mapeamento pendente. A origem no MV não foi confirmada.'
           end
    from ged.sector_document_type c
    where not exists (
        select 1 from ged.sector_type_mapping m where m.tenant_id = c.tenant_id and m.catalog_code = c.catalog_code
    );

    insert into ged.sector_profile (id, tenant_id, code, version_no, name, enabled)
    select gen_random_uuid(), t.id, p.code, 1, p.name, p.enabled
    from ged.tenant t
    cross join (values
        ('ANEXO_I', 'Organização do prontuário hospitalar — Anexo I', true),
        ('ANEXO_II', 'Composição física — Anexo II', false)
    ) as p(code, name, enabled)
    where not exists (
        select 1 from ged.sector_profile x where x.tenant_id = t.id and x.code = p.code and x.version_no = 1
    );

    insert into ged.sector_profile_item (profile_id, catalog_code, position)
    select p.id, c.catalog_code, c.catalog_order
    from ged.sector_profile p
    join ged.sector_document_type c on c.tenant_id = p.tenant_id
    where p.code = 'ANEXO_I' and p.version_no = 1
      and not exists (select 1 from ged.sector_profile_item i where i.profile_id = p.id and i.catalog_code = c.catalog_code);

    insert into ged.sector_profile_item (profile_id, catalog_code, position)
    select p.id, v.code, v.pos
    from ged.sector_profile p
    cross join (values
        ('DS01', 1), ('DS11', 2), ('DS12', 3), ('DS13', 4), ('DS14', 5), ('DS15', 6), ('DS16', 7), ('DS17', 8)
    ) as v(code, pos)
    where p.code = 'ANEXO_II' and p.version_no = 1
      and not exists (select 1 from ged.sector_profile_item i where i.profile_id = p.id and i.catalog_code = v.code);
end
$seed$;
