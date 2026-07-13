-- Diagnóstico de consolidação funcional e arquitetural — julho de 2026
-- Este script é somente leitura e não altera dados.
-- Ajuste nomes de schemas/tabelas se uma instalação legada possuir aliases diferentes.

-- 1. Documentos sem tenant
select id, name, created_at_utc
from ged.document
where tenant_id is null;

-- 2. Twins órfãos
select t.*
from ged.document_guardian_twin t
left join ged.document d on d.tenant_id = t.tenant_id and d.id = t.document_id
where d.id is null;

-- 3. Findings órfãos
select f.*
from ged.document_guardian_finding f
left join ged.document d on d.tenant_id = f.tenant_id and d.id = f.document_id
where d.id is null;

-- 4. Evidências órfãs
select e.*
from ged.document_guardian_evidence e
left join ged.document_guardian_finding f on f.tenant_id = e.tenant_id and f.id = e.finding_id
where f.id is null;

-- 5. Relacionamentos inválidos
select r.*
from ged.document_guardian_relationship r
left join ged.document source_doc on source_doc.tenant_id = r.tenant_id and source_doc.id = r.source_document_id
left join ged.document target_doc on target_doc.tenant_id = r.tenant_id and target_doc.id = r.target_document_id
where source_doc.id is null
   or target_doc.id is null
   or r.source_document_id = r.target_document_id;

-- 6. Obrigações sem documento
select o.*
from ged.document_guardian_obligation o
left join ged.document d on d.tenant_id = o.tenant_id and d.id = o.document_id
where d.id is null;

-- 7. Decisões sem finding
select d.*
from ged.document_guardian_decision d
left join ged.document_guardian_finding f on f.tenant_id = d.tenant_id and f.id = d.finding_id
where f.id is null;

-- 8. Duplicidade de twin por tenant/documento
select tenant_id, document_id, count(*) as total
from ged.document_guardian_twin
group by tenant_id, document_id
having count(*) > 1;

-- 9. Workers sem tenant válido
select w.*
from ged.worker_execution_state w
left join ged.tenant t on t.id = w.tenant_id
where w.tenant_id is null
   or t.id is null
   or coalesce(t.is_active, false) = false;

-- 10. Findings sem evidência
select f.*
from ged.document_guardian_finding f
left join ged.document_guardian_evidence e on e.tenant_id = f.tenant_id and e.finding_id = f.id
where e.id is null;
