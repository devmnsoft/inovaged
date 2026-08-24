insert into ged.uat_test_plan(plan_code,title,description,status,release_version)
values('UAT-DEFAULT','Plano padrão de homologação','Validação funcional assistida da entrega InovaGED.','DRAFT','2026.08')
on conflict do nothing;
with p as (select id from ged.uat_test_plan where plan_code='UAT-DEFAULT' and tenant_id is null and reg_status='A' limit 1),
c(module_code,case_code,title,preconditions,steps,expected_result,priority,display_order) as (values
('Administracao','UAT-ADM-001','Acessar Central de Administração','Usuário autorizado','Acessar /Administration','Painel carregado sem erro','CRITICAL',10),
('DatabaseReadiness','UAT-DB-001','Verificar pendências de migrations','Banco acessível','Acessar /DatabaseReadiness','Pendências críticas identificadas','CRITICAL',20),
('SchemaHealth','UAT-SCH-001','Validar saúde do schema','Migrations aplicadas','Acessar /SchemaHealth','Nenhum erro crítico','CRITICAL',30),
('Central de Incidentes','UAT-INC-001','Registrar e visualizar incidente técnico','Usuário autorizado','Registrar e abrir incidente','Incidente auditável e visível','HIGH',40),
('Etiquetas','UAT-LBL-001','Abrir PrintWizard sem erro','Caixa cadastrada','Abrir PrintWizard','Assistente carregado','HIGH',50),
('PrintWizard','UAT-LBL-002','Gerar etiqueta padrão de caixa','Impressora configurada','Selecionar caixa e gerar etiqueta','Prévia correta','HIGH',60),
('LocDesk','UAT-LBL-003','Gerar etiqueta LocDesk de caixa','Template LocDesk ativo','Gerar etiqueta LocDesk','Etiqueta compatível','HIGH',70),
('Acervo Físico','UAT-PHY-001','Abrir acervo físico e listar caixas','Acervo cadastrado','Acessar acervo físico','Caixas listadas','HIGH',80),
('Retenção','UAT-RET-001','Abrir módulo de retenção','PCD vigente','Acessar retenção','Painel carregado','HIGH',90),
('Instrumentos PCD/TTD/POP','UAT-INS-001','Abrir versões PCD','Instrumento cadastrado','Abrir versões PCD','Versões exibidas','HIGH',100),
('Empréstimos','UAT-LOA-001','Consultar empréstimos','Usuário autorizado','Abrir empréstimos','Solicitações exibidas','MEDIUM',110),
('OCR','UAT-OCR-001','Validar fila OCR','Documento elegível','Consultar processamento OCR','Status coerente','MEDIUM',120),
('Busca','UAT-SRC-001','Pesquisar documento','Documento indexado','Executar busca por título','Documento localizado','HIGH',130),
('SmartSearch','UAT-SMT-001','Executar SmartSearch','Índice disponível','Aplicar filtro e pesquisar','Resultados filtrados','MEDIUM',140),
('Documentos','UAT-DOC-001','Abrir documento','Documento ativo','Abrir detalhes e versão','Metadados e versão exibidos','CRITICAL',150),
('Protocolos','UAT-PRO-001','Consultar protocolos','Protocolo cadastrado','Abrir lista de protocolos','Protocolos exibidos','HIGH',160),
('Qualidade Documental','UAT-QUA-001','Validar painel de qualidade','Documento processado','Abrir qualidade documental','Indicadores exibidos','MEDIUM',170))
insert into ged.uat_test_case(plan_id,module_code,case_code,title,preconditions,steps,expected_result,priority,display_order)
select p.id,c.module_code,c.case_code,c.title,c.preconditions,c.steps,c.expected_result,c.priority,c.display_order from p cross join c
on conflict do nothing;
