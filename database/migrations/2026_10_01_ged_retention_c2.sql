-- Bloco C: temporalidade/retentao - evidencia de mudanca de regra TTD (v2)
-- e persistencia do motivo de bloqueio por item de lote de destinacao.
-- Aditiva e idempotente. Nao altera o comportamento existente das tabelas.
create schema if not exists ged;

-- C2: ao alterar uma regra de temporalidade (retention_rule_v2), preserva a evidencia da regra anterior.
alter table ged.retention_rule_v2 add column if not exists previous_values jsonb null;
alter table ged.retention_rule_v2 add column if not exists updated_by uuid null;

-- C2: itens de lote de destinacao registrados como bloqueados na execucao (emprestimo/movimentacao/protocolo/hold).
alter table ged.retention_destination_item add column if not exists block_reason text null;
alter table ged.retention_destination_item add column if not exists blocked_at timestamptz null;
