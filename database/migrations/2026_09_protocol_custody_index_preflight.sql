-- Enum-to-text casts are STABLE, so PostgreSQL rejects the published partial
-- index predicate. Direct enum comparisons are immutable and preserve its rule.
-- Duplicate live loans fail explicitly; no loan or historical evidence is removed.
create unique index if not exists ux_loan_request_open_protocol_request
on ged.loan_request (tenant_id, protocol_request_id)
where reg_status='A' and protocol_request_id is not null
  and status in ('REQUESTED','APPROVED','DELIVERED','OVERDUE');
