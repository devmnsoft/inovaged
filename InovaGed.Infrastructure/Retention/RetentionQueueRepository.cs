using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Retention;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace InovaGed.Infrastructure.Retention
{
    public sealed class RetentionQueueRepository : IRetentionQueueRepository
    {
        private readonly IDbConnectionFactory _db;
        private readonly ILogger<RetentionQueueRepository> _logger;

        public RetentionQueueRepository(IDbConnectionFactory db, ILogger<RetentionQueueRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<IReadOnlyList<RetentionRuleRow>> ListRulesAsync(Guid tenantId, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);
                var sql = """
            SELECT id,
                   class_code AS classcode,
                   start_event AS startevent,
                   current_days AS currentdays,
                   intermediate_days AS intermediatedays,
                   final_destination AS finaldestination,
                   notes
            FROM ged.retention_rule
            WHERE tenant_id=@TenantId AND reg_status='A'
            ORDER BY class_code;
            """;

                var rows = await con.QueryAsync<RetentionRuleRow>(new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct));
                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListRulesAsync failed Tenant={Tenant}", tenantId);
                throw;
            }
        }

        public async Task<Guid> UpsertRuleAsync(Guid tenantId, Guid? id, RetentionRuleRow rule, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);
                var rid = id ?? Guid.NewGuid();

                var sql = """
            INSERT INTO ged.retention_rule(id, tenant_id, class_code, start_event, current_days, intermediate_days, final_destination, notes)
            VALUES (@Id, @TenantId, @ClassCode, @StartEvent, @CurrentDays, @IntermediateDays, @FinalDestination, @Notes)
            ON CONFLICT (tenant_id, class_code) DO UPDATE SET
              start_event=EXCLUDED.start_event,
              current_days=EXCLUDED.current_days,
              intermediate_days=EXCLUDED.intermediate_days,
              final_destination=EXCLUDED.final_destination,
              notes=EXCLUDED.notes,
              reg_date=now(),
              reg_status='A'
            RETURNING id;
            """;

                var ret = await con.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
                {
                    Id = rid,
                    TenantId = tenantId,
                    ClassCode = rule.ClassCode.Trim(),
                    StartEvent = (rule.StartEvent ?? "CREATED").Trim().ToUpperInvariant(),
                    CurrentDays = rule.CurrentDays,
                    IntermediateDays = rule.IntermediateDays,
                    FinalDestination = (rule.FinalDestination ?? "ELIMINAR").Trim().ToUpperInvariant(),
                    Notes = rule.Notes
                }, cancellationToken: ct));

                return ret;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpsertRuleAsync failed Tenant={Tenant} Class={Class}", tenantId, rule.ClassCode);
                throw;
            }
        }

        /// <summary>
        /// Gera/atualiza a fila de temporalidade.
        /// PoC: implementação mínima e robusta:
        /// - Para cada documento ativo com class_code, calcula due_at = doc_created_at + (current+intermediate) dias
        /// - Insere na retention_queue se ainda não existir PENDING para o doc
        ///
        /// IMPORTANTE: aqui eu uso placeholders de colunas de documento:
        /// - ged.document: id, tenant_id, class_code, created_at, reg_status
        /// Ajuste os nomes conforme seu schema real (DocumentQueries).
        /// </summary>
        public async Task<int> GenerateQueueAsync(Guid tenantId, DateTimeOffset now, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);

                // ⚠ Ajuste para o seu schema real de documentos:
                // Se você tem "ged.document_versions" e "ged.documents", use a base correta.
                var sql = """
            WITH rules AS (
              SELECT class_code, (current_days + intermediate_days) AS total_days
              FROM ged.retention_rule
              WHERE tenant_id=@TenantId AND reg_status='A'
            ),
            docs AS (
              SELECT d.id AS document_id,
                     d.class_code,
                     d.created_at
              FROM ged.document d
              WHERE d.tenant_id=@TenantId AND d.reg_status='A'
                AND d.class_code IS NOT NULL AND d.class_code <> ''
            ),
            due AS (
              SELECT docs.document_id,
                     docs.class_code,
                     (docs.created_at + (rules.total_days || ' days')::interval) AS due_at
              FROM docs
              JOIN rules ON rules.class_code = docs.class_code
            )
            INSERT INTO ged.retention_queue(id, tenant_id, document_id, class_code, due_at, status, generated_at)
            SELECT gen_random_uuid(), @TenantId, due.document_id, due.class_code, due.due_at, 'PENDING', @Now
            FROM due
            WHERE due.due_at IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1 FROM ged.retention_queue q
                  WHERE q.tenant_id=@TenantId AND q.document_id=due.document_id
                    AND q.status IN ('PENDING','IN_TERM') AND q.reg_status='A'
              );
            """;

                // Dapper retorna linhas afetadas do INSERT
                var affected = await con.ExecuteAsync(new CommandDefinition(sql, new { TenantId = tenantId, Now = now }, cancellationToken: ct));
                return affected;
            }
            catch (PostgresException pg) when (pg.SqlState == "42P01")
            {
                // tabela/coluna não existe -> seu schema de docs é diferente.
                _logger.LogError(pg, "GenerateQueueAsync: tabela ged.document não existe ou colunas diferem. Ajuste o SQL para seu schema real.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateQueueAsync failed Tenant={Tenant}", tenantId);
                throw;
            }
        }

        public async Task<IReadOnlyList<RetentionQueueRow>> ListQueueAsync(Guid tenantId, string bucket, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);

                // bucket: "overdue" | "due30" | "due60" | "due90" | "all"
                var now = DateTimeOffset.UtcNow;

                string where = bucket.ToLowerInvariant() switch
                {
                    "overdue" => "q.due_at < @Now",
                    "due30" => "q.due_at >= @Now AND q.due_at < (@Now + interval '30 days')",
                    "due60" => "q.due_at >= @Now AND q.due_at < (@Now + interval '60 days')",
                    "due90" => "q.due_at >= @Now AND q.due_at < (@Now + interval '90 days')",
                    _ => "1=1"
                };

                var sql = $"""
            SELECT q.id, q.document_id AS documentid, q.class_code AS classcode,
                   q.due_at AS dueat, q.status, q.generated_at AS generatedat
            FROM ged.retention_queue q
            WHERE q.tenant_id=@TenantId AND q.reg_status='A'
              AND q.status='PENDING'
              AND {where}
            ORDER BY q.due_at ASC;
            """;

                var rows = await con.QueryAsync<RetentionQueueRow>(new CommandDefinition(sql, new { TenantId = tenantId, Now = now }, cancellationToken: ct));
                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListQueueAsync failed Tenant={Tenant} Bucket={Bucket}", tenantId, bucket);
                throw;
            }
        }

        public async Task MarkInTermAsync(Guid tenantId, IEnumerable<Guid> queueIds, CancellationToken ct)
        {
            var ids = queueIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
            if (ids.Length == 0) return;

            try
            {
                await using var con = await _db.OpenAsync(ct);
                await con.ExecuteAsync(new CommandDefinition("""
                UPDATE ged.retention_queue
                SET status='IN_TERM'
                WHERE tenant_id=@TenantId AND id=ANY(@Ids) AND reg_status='A';
            """, new { TenantId = tenantId, Ids = ids }, cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkInTermAsync failed Tenant={Tenant}", tenantId);
                throw;
            }
        }

        public async Task MarkDoneForDocumentsAsync(Guid tenantId, IEnumerable<Guid> documentIds, CancellationToken ct)
        {
            var ids = documentIds?.Distinct().ToArray() ?? Array.Empty<Guid>();
            if (ids.Length == 0) return;

            try
            {
                await using var con = await _db.OpenAsync(ct);
                await con.ExecuteAsync(new CommandDefinition("""
                UPDATE ged.retention_queue
                SET status='DONE'
                WHERE tenant_id=@TenantId AND document_id=ANY(@Ids) AND reg_status='A';
            """, new { TenantId = tenantId, Ids = ids }, cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkDoneForDocumentsAsync failed Tenant={Tenant}", tenantId);
                throw;
            }
        }
    }
}