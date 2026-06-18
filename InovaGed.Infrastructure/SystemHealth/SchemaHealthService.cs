using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.SystemHealth;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class SchemaHealthService : ISchemaHealthService
{
    private const string ConsolidationMigration = "database/migrations/2026_06_ged_schema_consolidation.sql";
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SchemaHealthService> _logger;
    private readonly ISchemaFixSqlProvider _fixSqlProvider;

    private static readonly string[] RequiredTables =
    [
        "ged.document", "ged.document_version", "ged.folder", "ged.document_search", "ged.ocr_job",
        "ged.code_sequence",
        "ged.upload_batch", "ged.upload_batch_item", "ged.upload_duplicate_decision", "ged.upload_session", "ged.upload_session_chunk",
        "ged.document_partial_part", "ged.audit_log", "ged.app_audit_log",
        "ged.loan_request", "ged.loan_request_item",
        "ged.document_quality_run", "ged.document_quality_result", "ged.loan_request_history",
        "ged.protocol_request", "ged.protocol_request_item", "ged.protocol_request_attachment", "ged.protocol_request_history"
    ];

    private static readonly string[] OptionalTables =
    [
        "ged.app_user", "ged.folder_virtual_map", "ged.ocr_auto_schedule_run", "ged.ocr_auto_schedule_run_item"
    ];

    private static readonly (string Table, string Column, string Area)[] RequiredColumns =
    [
        ("code_sequence", "id", "Code Generation"), ("code_sequence", "tenant_id", "Code Generation"),
        ("code_sequence", "entity_name", "Code Generation"), ("code_sequence", "prefix", "Code Generation"),
        ("code_sequence", "current_value", "Code Generation"), ("code_sequence", "padding", "Code Generation"),
        ("code_sequence", "created_at", "Code Generation"), ("code_sequence", "updated_at", "Code Generation"),
        ("code_sequence", "reg_status", "Code Generation"),
        ("folder", "updated_at", "GED folders"), ("folder", "updated_by", "GED folders"),
        ("document", "id", "GED base"), ("document", "tenant_id", "GED base"), ("document", "folder_id", "GED folders"),
        ("document", "current_version_id", "GED versions"), ("document", "created_at", "GED base"),
        ("document", "reg_status", "GED soft delete"), ("document", "deleted_at", "GED soft delete"),
        ("document", "deleted_by", "GED soft delete"), ("document", "deleted_reason", "GED soft delete"),
        ("document_version", "uploaded_at_utc", "GED versions"), ("document_version", "is_partial_document", "GED partial documents"),
        ("document_version", "partial_group_id", "GED partial documents"), ("document_version", "partial_part_number", "GED partial documents"),
        ("document_version", "partial_total_parts", "GED partial documents"), ("document_version", "partial_status", "GED partial documents"),
        ("document_version", "consolidated_version_id", "GED partial documents"),
        ("document", "is_document_incomplete", "GED incomplete documents"), ("document", "incomplete_reason", "GED incomplete documents"),
        ("document", "incomplete_source", "GED incomplete documents"), ("document_version", "incomplete_reason", "GED incomplete documents"),
        ("document_version", "incomplete_source", "GED incomplete documents"), ("upload_batch", "options_json", "Upload batch"),
        ("upload_batch_item", "upload_client_id", "Upload batch"), ("upload_batch_item", "content_hash", "Upload batch"), ("upload_batch_item", "duplicate_of_document_id", "Upload batch"), ("upload_batch_item", "duplicate_scope", "Upload batch"), ("upload_batch_item", "duplicate_resolution", "Upload batch"), ("upload_batch_item", "confirmed_duplicate_upload", "Upload batch"),
        ("upload_batch_item", "mark_as_incomplete", "Upload batch"), ("upload_batch_item", "retry_after_at", "Upload batch"),
        ("upload_batch_item", "processing_warning", "Upload batch"), ("document_search", "ocr_text", "OCR"),
        ("ocr_job", "id", "OCR"), ("ocr_job", "tenant_id", "OCR"), ("ocr_job", "document_version_id", "OCR"),
        ("ocr_job", "status", "OCR"), ("ocr_job", "requested_at", "OCR"), ("ocr_job", "started_at", "OCR"),
        ("ocr_job", "finished_at", "OCR"), ("ocr_job", "error_message", "OCR"), ("ocr_job", "error_details_json", "OCR"),
        ("ocr_job", "attempt_count", "OCR"), ("ocr_job", "worker_id", "OCR"), ("ocr_job", "locked_at", "OCR"),
        ("ocr_job", "locked_by", "OCR"), ("ocr_job", "updated_at", "OCR"), ("ocr_job", "next_attempt_at", "OCR"),
        ("ocr_job", "failure_code", "OCR"), ("ocr_job", "reg_status", "OCR"), ("ocr_job", "invalidate_digital_signatures", "OCR"),
        ("ocr_auto_schedule_run", "tenant_id", "OCR Auto Schedule"), ("ocr_auto_schedule_run", "started_at_utc", "OCR Auto Schedule"),
        ("ocr_auto_schedule_run", "status", "OCR Auto Schedule"), ("ocr_auto_schedule_run_item", "run_id", "OCR Auto Schedule"),
        ("ocr_auto_schedule_run_item", "status", "OCR Auto Schedule"),
        ("loan_request_item", "description", "Loans"), ("loan_request_item", "reference_code", "Loans"),
        ("loan_request_item", "is_manual", "Loans"), ("loan_request_item", "document_version_id", "Loans"),
        ("loan_request_item", "loan_request_id", "Loans"), ("loan_request_item", "reg_status", "Loans"),
        ("loan_request", "requester_sector", "Loans"), ("loan_request", "requester_sector_name", "Loans"),
        ("loan_request", "assigned_sector_name", "Loans"), ("loan_request", "current_sector_name", "Loans"),
        ("loan_request", "created_by", "Loans"), ("loan_request", "sector_id", "Loans"), ("loan_request", "protocol_request_id", "Loans"),
        ("loan_request_history", "tenant_id", "Loans History"), ("loan_request_history", "loan_request_id", "Loans History"),
        ("loan_request_history", "action", "Loans History"), ("loan_request_history", "old_status", "Loans History"),
        ("loan_request_history", "new_status", "Loans History"), ("loan_request_history", "user_id", "Loans History"),
        ("loan_request_history", "user_name", "Loans History"), ("loan_request_history", "sector_id", "Loans History"),
        ("loan_request_history", "sector_name", "Loans History"), ("loan_request_history", "reason", "Loans History"),
        ("loan_request_history", "internal_notes", "Loans History"), ("loan_request_history", "metadata_json", "Loans History"),
        ("loan_request_history", "correlation_id", "Loans History"), ("loan_request_history", "created_at", "Loans History"),
        ("loan_request_history", "reg_status", "Loans History"),

        ("protocol_request", "tenant_id", "Protocolo"), ("protocol_request", "protocol_no", "Protocolo"),
        ("protocol_request", "requester_user_id", "Protocolo"), ("protocol_request", "requester_name", "Protocolo"),
        ("protocol_request", "requester_sector_id", "Protocolo"), ("protocol_request", "requester_sector_name", "Protocolo"),
        ("protocol_request", "assigned_sector_id", "Protocolo"), ("protocol_request", "assigned_sector_name", "Protocolo"),
        ("protocol_request", "current_sector_name", "Protocolo"),
        ("protocol_request", "assigned_user_id", "Protocolo"), ("protocol_request", "assigned_user_name", "Protocolo"),
        ("protocol_request", "title", "Protocolo"), ("protocol_request", "description", "Protocolo"),
        ("protocol_request", "priority", "Protocolo"), ("protocol_request", "status", "Protocolo"),
        ("protocol_request", "due_at", "Protocolo"), ("protocol_request", "requested_at", "Protocolo"),
        ("protocol_request", "updated_at", "Protocolo"), ("protocol_request", "finished_at", "Protocolo"),
        ("protocol_request", "reg_status", "Protocolo"), ("protocol_request", "correlation_id", "Protocolo"),
        ("protocol_request", "created_at", "Protocolo"),
        ("protocol_request_item", "tenant_id", "Protocolo"), ("protocol_request_item", "protocol_request_id", "Protocolo"),
        ("protocol_request_item", "document_id", "Protocolo"), ("protocol_request_item", "document_version_id", "Protocolo"),
        ("protocol_request_item", "is_manual", "Protocolo"), ("protocol_request_item", "reference_code", "Protocolo"),
        ("protocol_request_item", "description", "Protocolo"), ("protocol_request_item", "document_type", "Protocolo"),
        ("protocol_request_item", "patient_name", "Protocolo"), ("protocol_request_item", "medical_record_number", "Protocolo"),
        ("protocol_request_item", "box_code", "Protocolo"), ("protocol_request_item", "physical_location", "Protocolo"),
        ("protocol_request_item", "notes", "Protocolo"), ("protocol_request_item", "created_at", "Protocolo"),
        ("protocol_request_item", "reg_status", "Protocolo"),
        ("protocol_request_attachment", "tenant_id", "Protocolo"), ("protocol_request_attachment", "protocol_request_id", "Protocolo"),
        ("protocol_request_attachment", "file_name", "Protocolo"), ("protocol_request_attachment", "content_type", "Protocolo"),
        ("protocol_request_attachment", "size_bytes", "Protocolo"), ("protocol_request_attachment", "storage_path", "Protocolo"),
        ("protocol_request_attachment", "uploaded_by", "Protocolo"), ("protocol_request_attachment", "uploaded_by_name", "Protocolo"),
        ("protocol_request_attachment", "uploaded_at", "Protocolo"), ("protocol_request_attachment", "reg_status", "Protocolo"),
        ("protocol_request_history", "tenant_id", "Protocolo"), ("protocol_request_history", "protocol_request_id", "Protocolo"),
        ("protocol_request_history", "action", "Protocolo"), ("protocol_request_history", "old_status", "Protocolo"),
        ("protocol_request_history", "new_status", "Protocolo"), ("protocol_request_history", "user_id", "Protocolo"),
        ("protocol_request_history", "user_name", "Protocolo"), ("protocol_request_history", "sector_id", "Protocolo"),
        ("protocol_request_history", "sector_name", "Protocolo"), ("protocol_request_history", "reason", "Protocolo"),
        ("protocol_request_history", "internal_notes", "Protocolo"), ("protocol_request_history", "metadata_json", "Protocolo"),
        ("protocol_request_history", "correlation_id", "Protocolo"), ("protocol_request_history", "created_at", "Protocolo"),
        ("protocol_request_history", "reg_status", "Protocolo"),
        ("upload_batch", "id", "GED Upload History"), ("upload_batch", "tenant_id", "GED Upload History"),
        ("upload_batch", "created_by", "GED Upload History"), ("upload_batch", "created_by_name", "GED Upload History"),
        ("upload_batch", "folder_id", "GED Upload History"), ("upload_batch", "requested_folder_id", "GED Upload History"),
        ("upload_batch", "status", "GED Upload History"), ("upload_batch", "total_files", "GED Upload History"),
        ("upload_batch", "success_files", "GED Upload History"), ("upload_batch", "failed_files", "GED Upload History"),
        ("upload_batch", "skipped_files", "GED Upload History"), ("upload_batch", "source_ip", "GED Upload History"),
        ("upload_batch", "user_agent", "GED Upload History"), ("upload_batch", "correlation_id", "GED Upload History"),
        ("upload_batch", "started_at", "GED Upload History"), ("upload_batch", "finished_at", "GED Upload History"),
        ("upload_batch", "created_at", "GED Upload History"), ("upload_batch", "updated_at", "GED Upload History"),
        ("upload_batch", "reg_status", "GED Upload History"), ("upload_batch", "acknowledged_at", "GED Upload History"),
        ("upload_batch", "acknowledged_by", "GED Upload History"), ("upload_batch", "problem_seen", "GED Upload History"),
        ("upload_batch", "user_notes", "GED Upload History"), ("upload_batch_item", "id", "GED Upload History"),
        ("upload_batch_item", "tenant_id", "GED Upload History"), ("upload_batch_item", "batch_id", "GED Upload History"),
        ("upload_batch_item", "document_id", "GED Upload History"), ("upload_batch_item", "version_id", "GED Upload History"),
        ("upload_batch_item", "original_file_name", "GED Upload History"), ("upload_batch_item", "stored_file_name", "GED Upload History"),
        ("upload_batch_item", "status", "GED Upload History"), ("upload_batch_item", "error_message", "GED Upload History"),
        ("upload_batch_item", "error_step", "GED Upload History"), ("upload_batch_item", "can_retry", "GED Upload History"),
        ("upload_batch_item", "size_bytes", "GED Upload History"), ("upload_batch_item", "elapsed_ms", "GED Upload History"),
        ("upload_batch_item", "processing_warning", "GED Upload History"), ("upload_batch_item", "correlation_id", "GED Upload History"),
        ("upload_batch_item", "created_at", "GED Upload History"), ("upload_batch_item", "finished_at", "GED Upload History"),
        ("upload_batch_item", "updated_at", "GED Upload History"), ("upload_batch_item", "reg_status", "GED Upload History"),
        ("upload_batch_item", "uploaded_by_name", "GED Upload History"), ("upload_batch_item", "upload_session_id", "Upload batch"),
        ("upload_session", "tenant_id", "Upload chunks"), ("upload_session", "total_chunks", "Upload chunks"),
        ("upload_session_chunk", "session_id", "Upload chunks"), ("upload_session_chunk", "chunk_index", "Upload chunks"),
        ("app_audit_log", "created_at", "SystemLogs"), ("app_audit_log", "user_name", "SystemLogs"),
        ("audit_log", "created_at", "SystemLogs"), ("audit_log", "user_name", "SystemLogs"),
        ("document_quality_run", "tenant_id", "Qualidade Documental"), ("document_quality_run", "started_at_utc", "Qualidade Documental"),
        ("document_quality_run", "status", "Qualidade Documental"), ("document_quality_run", "total_documents", "Qualidade Documental"),
        ("document_quality_run", "excellent_count", "Qualidade Documental"), ("document_quality_run", "good_count", "Qualidade Documental"),
        ("document_quality_run", "warning_count", "Qualidade Documental"), ("document_quality_run", "critical_count", "Qualidade Documental"),
        ("document_quality_run", "failed_count", "Qualidade Documental"),
        ("document_quality_result", "tenant_id", "Qualidade Documental"), ("document_quality_result", "document_id", "Qualidade Documental"),
        ("document_quality_result", "quality_score", "Qualidade Documental"), ("document_quality_result", "quality_status", "Qualidade Documental"),
        ("document_quality_result", "has_ocr", "Qualidade Documental"), ("document_quality_result", "has_ocr_error", "Qualidade Documental"),
        ("document_quality_result", "has_classification", "Qualidade Documental"), ("document_quality_result", "has_document_type", "Qualidade Documental"),
        ("document_quality_result", "has_required_metadata", "Qualidade Documental"), ("document_quality_result", "is_partial_document", "Qualidade Documental"),
        ("document_quality_result", "is_partial_incomplete", "Qualidade Documental"), ("document_quality_result", "is_ready_to_consolidate", "Qualidade Documental"),
        ("document_quality_result", "is_consolidated", "Qualidade Documental"), ("document_quality_result", "has_possible_duplicate", "Qualidade Documental"),
        ("document_quality_result", "has_lgpd_risk", "Qualidade Documental"), ("document_quality_result", "issues_json", "Qualidade Documental"),
        ("document_quality_result", "recommendations_json", "Qualidade Documental"), ("document_quality_result", "analyzed_at_utc", "Qualidade Documental")
    ];

    private static readonly (string Name, string[] Alternatives, string Message)[] RecommendedIndexes =
    [
        ("ix_document_tenant_reg_status", [], "Índice de exclusão lógica por tenant/status."),
        ("ix_document_tenant_folder_reg_status", [], "Índice de navegação por tenant/pasta/status lógico."),
        ("ix_document_deleted_at", [], "Índice parcial para auditoria/expurgo futuro de documentos excluídos."),
        ("ix_document_tenant_folder_status", ["ix_document_tenant_folder_reg_status"], "Índice de navegação por tenant/pasta/status."),
        ("ix_document_current_version", ["ix_document_version_document_current"], "Índice de resolução da versão atual."),
        ("ix_document_version_partial_group_id", [], "Índice para documentos fracionados por grupo."),
        ("ix_document_version_partial_status", [], "Índice para filtro por status parcial."),
        ("ix_document_version_uploaded_at_utc", [], "Índice para ordenação por upload."),
        ("ix_upload_batch_tenant_status", [], "Índice do upload em lote."),
        ("ix_upload_batch_item_status", [], "Índice dos itens de upload em lote."),
        ("ix_upload_batch_item_upload_session", [], "Índice de vínculo entre lote e sessão chunked."),
        ("ix_upload_batch_item_tenant_batch_client", [], "Índice de idempotência por cliente no upload em lote."),
        ("ix_upload_batch_item_tenant_duplicate", [], "Índice de vínculo de possível duplicidade no upload."),
        ("ix_upload_duplicate_decision_tenant_batch", [], "Índice de auditoria de decisões de duplicidade por lote."),
        ("ix_upload_session_tenant_user_status", [], "Índice de sessões chunked por usuário/status."),
        ("ix_upload_session_chunk_session", [], "Índice dos chunks por sessão."),
        ("ix_ocr_job_tenant_version_status", [], "Índice da fila OCR por versão/status."),
        ("ix_ocr_auto_schedule_run_tenant_started", [], "Índice do histórico do agendamento automático de OCR."),
        ("ix_ocr_auto_schedule_run_item_run", [], "Índice dos itens do agendamento automático de OCR."),
        ("ix_ocr_auto_schedule_run_status", [], "Índice de status do agendamento automático de OCR."),
        ("ix_loan_request_item_loan_request", [], "Índice dos itens por solicitação de empréstimo."),
        ("ix_loan_request_item_document", [], "Índice dos itens por documento GED."),
        ("ix_loan_request_item_manual", [], "Índice dos itens manuais/físicos de empréstimo."),
        ("ix_loan_request_history_tenant_loan_created", [], "Índice do histórico de empréstimo por solicitação/data."),
        ("ix_loan_request_history_tenant_action", [], "Índice do histórico de empréstimo por ação."),
        ("ix_loan_request_history_tenant_created", [], "Índice do histórico de empréstimo por data."),
        ("ix_loan_request_history_tenant_user", [], "Índice do histórico de empréstimo por usuário."),
        ("ux_protocol_request_tenant_protocol_no", [], "Índice único do protocolo por tenant/número."),
        ("ix_protocol_request_tenant_status", [], "Índice do protocolo por tenant/status."),
        ("ix_protocol_request_tenant_requester", [], "Índice do protocolo por solicitante."),
        ("ix_protocol_request_tenant_assigned_sector", [], "Índice do protocolo por setor atribuído."),
        ("ix_protocol_request_tenant_assigned_user", [], "Índice do protocolo por usuário atribuído."),
        ("ix_protocol_request_tenant_requested_at", [], "Índice do protocolo por data solicitada."),
        ("ix_protocol_request_item_protocol", [], "Índice de itens por protocolo."),
        ("ix_protocol_request_attachment_protocol", [], "Índice de anexos por protocolo."),
        ("ix_protocol_request_history_protocol_created", [], "Índice de histórico por protocolo/data."),
        ("ix_loan_request_protocol_request", [], "Índice de vínculo empréstimo/protocolo."),
        ("ix_loan_request_tenant_requester_sector_name", [], "Índice de empréstimos por setor solicitante."),
        ("ix_loan_request_tenant_assigned_sector_name", [], "Índice de empréstimos por setor atribuído."),
        ("ix_loan_request_tenant_current_sector_name", [], "Índice de empréstimos por setor atual."),
        ("ix_loan_request_tenant_requester_id", [], "Índice de empréstimos por solicitante."),
        ("ix_loan_request_tenant_created_by", [], "Índice de empréstimos por criador."),
        ("ix_protocol_request_tenant_assigned_sector_name", [], "Índice de protocolos por setor atribuído."),
        ("ix_protocol_request_tenant_requester_sector_name", [], "Índice de protocolos por setor solicitante."),
        ("ix_protocol_request_tenant_requester_user_id", [], "Índice de protocolos por usuário solicitante."),
        ("ix_app_audit_log_tenant_created", [], "Índice de auditoria por tenant/data."),
        ("ix_app_audit_log_user_created", [], "Índice de auditoria por usuário/data."),
        ("ix_app_audit_log_action_created", [], "Índice de auditoria por ação/data."),
        ("ix_app_audit_log_correlation", [], "Índice de auditoria por correlationId."),
        ("ix_document_quality_result_tenant_document_analyzed", [], "Índice de qualidade documental por tenant/documento/análise."),
        ("ix_document_quality_result_tenant_status", [], "Índice de qualidade documental por tenant/status."),
        ("ix_document_quality_result_tenant_score", [], "Índice de qualidade documental por tenant/score."),
        ("ix_document_quality_result_tenant_has_ocr", [], "Índice de qualidade documental por tenant/OCR."),
        ("ix_document_quality_result_tenant_lgpd", [], "Índice de qualidade documental por tenant/risco LGPD."),
        ("ix_document_quality_result_run", [], "Índice de resultados por execução de qualidade documental."),
        ("ix_document_quality_run_tenant_started", [], "Índice de execuções de qualidade documental por tenant/data."),
        ("ix_document_quality_run_status", [], "Índice de execuções de qualidade documental por tenant/status.")
    ];

    public SchemaHealthService(IDbConnectionFactory db, ILogger<SchemaHealthService> logger, ISchemaFixSqlProvider fixSqlProvider)
    {
        _db = db;
        _logger = logger;
        _fixSqlProvider = fixSqlProvider;
    }

    public async Task<SchemaHealthReportDto> CheckAsync(CancellationToken ct)
    {
        var report = new SchemaHealthReportDto();

        try
        {
            await using var conn = await _db.OpenAsync(ct);

            var existingTables = (await conn.QueryAsync<string>(new CommandDefinition(@"
select table_schema || '.' || table_name
from information_schema.tables
where table_schema = 'ged';", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var table in RequiredTables)
            {
                var ok = existingTables.Contains(table);
                var tableFix = string.Equals(table, "ged.loan_request_history", StringComparison.OrdinalIgnoreCase)
                    ? "Execute database/migrations/2026_06_loans_history.sql ou database/apply_all_required_migrations.sql."
                    : $"Execute o SQL específico desta linha ou {ConsolidationMigration}.";
                AddCheck(report, BuildTableId(table), "GED", table, "Tabela", "Critical", ok, ok ? "Tabela crítica encontrada." : "Tabela crítica ausente.", tableFix);
                if (!ok) report.MissingTables.Add(table);
            }

            foreach (var table in OptionalTables)
            {
                var ok = existingTables.Contains(table);
                var isOcrAutoSchedule = table.Contains("ocr_auto_schedule", StringComparison.OrdinalIgnoreCase);
                AddCheck(report, BuildTableId(table), isOcrAutoSchedule ? "OCR Auto Schedule" : "GED", table, "Tabela", isOcrAutoSchedule ? "Warning" : "Info", ok, ok ? "Tabela auxiliar encontrada." : isOcrAutoSchedule ? "Tabela de agendamento OCR ausente; a Central OCR continua funcional, mas /Ocr/AutoSchedule exige migrations." : "Tabela auxiliar ausente; não bloqueia GED, OCR, upload ou logs.", $"Execute o SQL específico desta linha ou {ConsolidationMigration} se este recurso auxiliar for usado no ambiente.");
            }

            var existingColumns = (await conn.QueryAsync<string>(new CommandDefinition(@"
select table_name || '.' || column_name
from information_schema.columns
where table_schema = 'ged';", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (table, column, area) in RequiredColumns)
            {
                var objectName = $"ged.{table}.{column}";
                var ok = existingColumns.Contains($"{table}.{column}");
                var severity = string.Equals(area, "OCR Auto Schedule", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Critical";
                var missingMessage = severity == "Warning" ? "Coluna de agendamento OCR ausente; a Central OCR funciona, mas a página de agendamento exige migration." : "Coluna crítica ausente.";
                AddCheck(report, BuildColumnId(table, column), area, objectName, "Coluna", severity, ok, ok ? "Coluna encontrada." : missingMessage, $"Execute o SQL específico desta linha ou {ConsolidationMigration}.");
                if (!ok) report.MissingColumns.Add(objectName);
            }

            if (existingTables.Contains("ged.document_search"))
            {
                var documentSearchColumns = (await conn.QueryAsync<DocumentSearchColumnInfo>(new CommandDefinition(@"
select column_name as ""ColumnName"", data_type as ""DataType""
from information_schema.columns
where table_schema = 'ged'
  and table_name = 'document_search'
order by ordinal_position;", cancellationToken: ct))).ToList();
                report.Recommendations.Add("Diagnóstico ged.document_search: " + string.Join(", ", documentSearchColumns.Select(c => $"{c.ColumnName} ({c.DataType})")));
            }

            var existingIndexRows = (await conn.QueryAsync<PgIndexInfo>(new CommandDefinition(@"
select indexname as ""IndexName"", indexdef as ""IndexDef""
from pg_indexes
where schemaname = 'ged';", cancellationToken: ct))).ToList();
            var existingIndexes = existingIndexRows.Select(i => i.IndexName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            AddDocumentSearchTenantVersionIndexCheck(report, existingColumns, existingIndexRows);
            AddCheck(report, "SMART_SEARCH_CONTEXT_TERM", "SmartSearch", "ged.search_context_term", "Tabela", "Warning", existingTables.Contains("ged.search_context_term"), "Dicionário de contexto da busca inteligente disponível.", "Execute database/migrations/2026_06_finalize_loans_secure_document_sharing.sql.");
            AddCheck(report, "LOANS_SECURE_DOCUMENT_LINK", "Loans", "ged.secure_document_link", "Tabela", "Warning", existingTables.Contains("ged.secure_document_link"), "Links seguros de entrega digital disponíveis.", "Execute database/migrations/2026_06_fix_secure_document_link_sharing.sql.");
            AddCheck(report, "LOANS_SECURE_DOCUMENT_LINK_ACCESS", "Loans", "ged.secure_document_link_access", "Tabela", "Warning", existingTables.Contains("ged.secure_document_link_access"), "Auditoria de acessos a links seguros disponível.", "Execute database/migrations/2026_06_fix_secure_document_link_sharing.sql.");

            foreach (var (name, alternatives, message) in RecommendedIndexes)
            {
                var foundName = existingIndexes.Contains(name) ? name : alternatives.FirstOrDefault(existingIndexes.Contains);
                var ok = !string.IsNullOrWhiteSpace(foundName);
                AddCheck(report, BuildIndexId(name), "Performance", $"ged.{name}", "Índice", "Warning", ok,
                    ok ? $"{message} OK ({foundName})." : $"{message} Ausente ou não aplicável por diferença de colunas no ambiente.",
                    $"Use o botão 'Copiar SQL de correção' e execute {ConsolidationMigration}; índices opcionais são criados somente quando as colunas existem.");
            }


            if (existingTables.Contains("ged.upload_batch_item"))
            {
                var constraintDef = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(@"
select pg_get_constraintdef(oid)
from pg_constraint
where conrelid = 'ged.upload_batch_item'::regclass
  and conname = 'ck_upload_batch_item_status';", cancellationToken: ct));
                var requiredStatuses = new[] { "PENDING", "RECEIVING", "SAVED", "DOCUMENT_CREATED", "QUEUED", "COMPLETED", "ERROR", "SKIPPED", "ABORTED", "RETRYABLE", "DUPLICATE", "CANCELLED" };
                var constraintOk = !string.IsNullOrWhiteSpace(constraintDef) && requiredStatuses.All(status => constraintDef.Contains($"'{status}'", StringComparison.OrdinalIgnoreCase));
                AddCheck(report, "GED_CONSTRAINT_UPLOAD_BATCH_ITEM_STATUS", "Upload batch", "ged.upload_batch_item.ck_upload_batch_item_status", "Constraint", "Critical", constraintOk,
                    constraintOk ? "Constraint de status do upload em lote compatível." : "Constraint ck_upload_batch_item_status antiga ou ausente; status atuais do upload em lote podem falhar com 23514.",
                    "Execute database/migrations/2026_06_fix_upload_batch_item_status_constraint.sql ou aplique o SchemaRepair.");
            }

            var existingEnums = (await conn.QueryAsync<string>(new CommandDefinition(@"
select n.nspname || '.' || t.typname
from pg_type t
join pg_namespace n on n.oid = t.typnamespace
where n.nspname = 'ged' and t.typname in ('document_status_enum','document_visibility_enum','ocr_status_enum','preview_processing_status');", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var enumName in new[] { "ged.document_status_enum", "ged.document_visibility_enum", "ged.ocr_status_enum" })
            {
                var ok = existingEnums.Contains(enumName);
                AddCheck(report, BuildGenericId("GED_ENUM", enumName), "Enums", enumName, "Enum", "Critical", ok, ok ? "Enum encontrado." : "Enum crítico usado pelo código não detectado.", "Aplique as migrations base e a consolidação do GED para criar os enums PostgreSQL usados pelo código.");
            }

            var migrationRegistered = existingTables.Contains("ged.schema_migration_history") || await conn.ExecuteScalarAsync<bool>(new CommandDefinition(@"
select exists (
    select 1
    from information_schema.tables
    where table_schema = 'public'
      and table_name in ('schema_migrations','__efmigrationshistory','migration_history')
);", cancellationToken: ct));
            AddCheck(report, "GED_TABLE_SCHEMA_MIGRATION_HISTORY", "Migrations", "ged.schema_migration_history", "Diagnóstico", "Warning", migrationRegistered,
                migrationRegistered ? "Histórico de migrations encontrado." : "Histórico padronizado ainda não detectado.",
                "O script consolidado cria ged.schema_migration_history e registra a aplicação.");

            if (existingTables.Contains("ged.schema_migration_history"))
            {
                report.LastMigration = await conn.QueryFirstOrDefaultAsync<SchemaMigrationHistoryDto>(new CommandDefinition(@"
select script_name as ""ScriptName"",
       applied_at as ""AppliedAt"",
       applied_by as ""AppliedBy"",
       success as ""Success"",
       notes as ""Notes""
from ged.schema_migration_history
where success = true
order by applied_at desc
limit 1;", cancellationToken: ct));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao executar diagnóstico de schema do banco.");
            AddCheck(report, "GED_DIAGNOSTIC_DATABASE", "Banco", "Conexão/diagnóstico", "Erro", "Critical", false, "Falha ao consultar metadados do banco.", "Verifique a connection string e permissões de information_schema/pg_catalog.");
        }

        await EnrichFixesAsync(report, ct);

        report.IsHealthy = !report.Checks.Any(c => !c.Success && c.Severity == "Critical");
        var hasRecommendedFailures = report.Checks.Any(c => !c.Success && c.Severity == "Warning");
        report.Recommendations.Add(report.IsHealthy
            ? (hasRecommendedFailures
                ? "Schema funcional com recomendações de performance."
                : "Schema compatível: tabelas e colunas críticas necessárias ao GED/OCR/upload/logs foram encontradas.")
            : $"Execute o script consolidado {ConsolidationMigration} ou o master database/apply_all_required_migrations.sql.");
        report.Recommendations.Add("Use 'Copiar SQL de correção' para obter o comando psql recomendado e reabra /SystemHealth/Schema após aplicar.");
        report.Recommendations.Add("Pendências recomendadas são índices/performance; pendências opcionais são histórico/melhorias operacionais.");

        return report;
    }

    private async Task EnrichFixesAsync(SchemaHealthReportDto report, CancellationToken ct)
    {
        var fixes = await _fixSqlProvider.GetFixesAsync(report, ct);
        var byId = fixes.ToDictionary(f => f.CheckId, StringComparer.OrdinalIgnoreCase);

        foreach (var check in report.Checks.Where(c => !c.Success))
        {
            if (!byId.TryGetValue(check.Id, out var fix))
                continue;

            var canAutoFix = fix.CanAutoFix;
            if (string.Equals(check.Id, "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION", StringComparison.OrdinalIgnoreCase)
                && check.Message.Contains("Correção automática indisponível", StringComparison.OrdinalIgnoreCase))
            {
                canAutoFix = false;
            }

            check.CanAutoFix = canAutoFix;
            check.FixSql = fix.FixSql;
            check.FixScriptName = fix.ScriptName;
            check.RiskLevel = fix.RiskLevel;
            check.Dependencies = fix.Dependencies.Select(FormatDependency).ToArray();
            check.FixSuggestion = fix.Description;
        }
    }

    private static string FormatDependency(SchemaObjectDependency dependency)
    {
        return dependency.Type.ToLowerInvariant() switch
        {
            "schema" => $"schema {dependency.Schema}",
            "table" => $"table {dependency.Schema}.{dependency.Table}",
            "column" => $"column {dependency.Schema}.{dependency.Table}.{dependency.Column}{(dependency.Required ? string.Empty : " (alternativa)")}",
            "extension" => $"extension {dependency.Schema}",
            _ => $"{dependency.Type} {dependency.Schema}.{dependency.Table}.{dependency.Column}".Trim('.')
        };
    }


    private static void AddDocumentSearchTenantVersionIndexCheck(SchemaHealthReportDto report, HashSet<string> existingColumns, IReadOnlyList<PgIndexInfo> existingIndexes)
    {
        const string checkId = "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION";
        const string objectName = "ged.ix_ged_document_search_tenant_version";
        const string missingLinkMessage = "Não foi encontrada coluna de vínculo documental em ged.document_search. Verifique o modelo de busca. Correção automática indisponível: não há coluna document_version_id, version_id ou document_id em ged.document_search.";
        const string noAutofixSuggestion = "Não foi possível gerar correção automática porque nenhuma coluna compatível foi encontrada.";

        var hasTenantId = HasColumn(existingColumns, "document_search", "tenant_id");
        var candidates = new[]
        {
            new DocumentSearchIndexCandidate("document_version_id", "ix_ged_document_search_tenant_document_version"),
            new DocumentSearchIndexCandidate("version_id", "ix_ged_document_search_tenant_version"),
            new DocumentSearchIndexCandidate("document_id", "ix_ged_document_search_tenant_document")
        };

        var availableCandidates = candidates.Where(c => HasColumn(existingColumns, "document_search", c.ColumnName)).ToList();
        if (!hasTenantId || availableCandidates.Count == 0)
        {
            AddCheck(report, checkId, "Performance", objectName, "Índice", "Warning", false, missingLinkMessage, noAutofixSuggestion);
            return;
        }

        var indexedCandidate = availableCandidates.FirstOrDefault(c => HasDocumentSearchIndex(existingIndexes, c.ColumnName));
        if (indexedCandidate is not null)
        {
            AddCheck(report, checkId, "Performance", objectName, "Índice", "Warning", true,
                $"Índice de busca OCR por tenant/{indexedCandidate.ColumnName} OK.",
                "Índice recomendado já existe para a coluna documental compatível.");
            return;
        }

        var selectedCandidate = availableCandidates[0];
        AddCheck(report, checkId, "Performance", objectName, "Índice", "Warning", false,
            $"Índice de busca OCR ausente. A correção automática usará tenant_id + {selectedCandidate.ColumnName} em ged.document_search.",
            $"Será criado índice recomendado usando tenant_id + {selectedCandidate.ColumnName}; índices opcionais são criados somente quando as colunas existem.");
    }

    private static bool HasColumn(HashSet<string> existingColumns, string table, string column)
        => existingColumns.Contains($"{table}.{column}");

    private static bool HasDocumentSearchIndex(IReadOnlyList<PgIndexInfo> existingIndexes, string columnName)
    {
        var expectedColumns = $"(tenant_id, {columnName})";
        return existingIndexes.Any(index =>
            index.IndexDef.Contains(" on ged.document_search ", StringComparison.OrdinalIgnoreCase)
            && index.IndexDef.Contains(expectedColumns, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class PgIndexInfo
    {
        public string IndexName { get; set; } = string.Empty;
        public string IndexDef { get; set; } = string.Empty;
    }

    private sealed class DocumentSearchColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
    }

    private sealed record DocumentSearchIndexCandidate(string ColumnName, string IndexName);

    private static void AddCheck(SchemaHealthReportDto report, string id, string area, string objectName, string checkType, string severity, bool success, string message, string fixSuggestion)
    {
        report.Checks.Add(new SchemaCheckItemDto
        {
            Id = id,
            Area = area,
            ObjectName = objectName,
            CheckType = checkType,
            Severity = severity,
            Success = success,
            Message = message,
            FixSuggestion = success ? string.Empty : fixSuggestion
        });
    }

    private static string BuildTableId(string table)
    {
        return table.ToLowerInvariant() switch
        {
            "ged.app_audit_log" => "GED_TABLE_APP_AUDIT_LOG",
            "ged.schema_migration_history" => "GED_TABLE_SCHEMA_MIGRATION_HISTORY",
            "ged.upload_session" => "GED_TABLE_UPLOAD_SESSION",
            "ged.upload_session_chunk" => "GED_TABLE_UPLOAD_SESSION_CHUNK",
            "ged.document_partial_part" => "GED_TABLE_DOCUMENT_PARTIAL_PART",
            "ged.document_quality_run" => "GED_TABLE_DOCUMENT_QUALITY_RUN",
            "ged.document_quality_result" => "GED_TABLE_DOCUMENT_QUALITY_RESULT",
            "ged.loan_request_history" => "GED_TABLE_LOAN_REQUEST_HISTORY",
            _ => BuildGenericId("GED_TABLE", table)
        };
    }

    private static string BuildColumnId(string table, string column) => $"GED_COLUMN_{table.ToUpperInvariant()}_{column.ToUpperInvariant()}";

    private static string BuildIndexId(string indexName)
    {
        return indexName.ToLowerInvariant() switch
        {
            "ix_document_tenant_folder_status" => "GED_INDEX_DOCUMENT_TENANT_FOLDER_STATUS",
            "ix_document_version_document_current" => "GED_INDEX_DOCUMENT_VERSION_DOCUMENT_CURRENT",
            "ix_document_current_version" => "GED_INDEX_DOCUMENT_CURRENT_VERSION",
            "ix_document_version_partial_group_id" => "GED_INDEX_DOCUMENT_VERSION_PARTIAL_GROUP_ID",
            "ix_document_version_partial_status" => "GED_INDEX_DOCUMENT_VERSION_PARTIAL_STATUS",
            "ix_document_version_uploaded_at_utc" => "GED_INDEX_DOCUMENT_VERSION_UPLOADED_AT_UTC",
            "ix_ged_document_search_tenant_version" => "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION",
            "ix_ged_document_search_tenant_document_version" => "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION",
            "ix_ged_document_search_tenant_document" => "GED_INDEX_DOCUMENT_SEARCH_TENANT_VERSION",
            "ix_upload_session_tenant_user_status" => "GED_INDEX_UPLOAD_SESSION_TENANT_USER_STATUS",
            "ix_upload_session_chunk_session" => "GED_INDEX_UPLOAD_SESSION_CHUNK_SESSION",
            "ix_app_audit_log_tenant_created" => "GED_INDEX_APP_AUDIT_LOG_TENANT_CREATED",
            "ix_app_audit_log_user_created" => "GED_INDEX_APP_AUDIT_LOG_USER_CREATED",
            "ix_app_audit_log_action_created" => "GED_INDEX_APP_AUDIT_LOG_ACTION_CREATED",
            "ix_app_audit_log_correlation" => "GED_INDEX_APP_AUDIT_LOG_CORRELATION",
            _ => BuildGenericId("GED_INDEX", indexName)
        };
    }

    private static string BuildGenericId(string prefix, string name)
        => $"{prefix}_{name.Replace("ged.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace('.', '_').Replace('-', '_').ToUpperInvariant()}";
}
