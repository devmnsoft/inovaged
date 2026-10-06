using System.Diagnostics;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace InovaGed.Application.Tests;

[Collection("Document AI PostgreSQL")]
public sealed class HospitalDocumentsSearchBenchmarkTests(ITestOutputHelper output)
{
    [PgGatedFact]
    public async Task Measure_authorized_search_baseline_vs_pushdown_predicate()
    {
        var dbName = "bench_search_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(PgGate.Dsn());
        await admin.OpenAsync();
        await admin.ExecuteAsync($"create database {dbName}");
        try
        {
            var dsn = new NpgsqlConnectionStringBuilder(PgGate.Dsn()) { Database = dbName, Pooling = false }.ConnectionString;
            await using var conn = new NpgsqlConnection(dsn);
            await conn.OpenAsync();

            // 1. Setup schema
            await conn.ExecuteAsync("""
CREATE SCHEMA ged;
CREATE TYPE ged.document_status_enum AS ENUM ('DRAFT','ACTIVE','ARCHIVED','DELETED');

CREATE TABLE ged.tenant (id uuid PRIMARY KEY, name text, code text);
CREATE TABLE ged.app_user (id uuid PRIMARY KEY, tenant_id uuid, reg_status text, is_active boolean);
CREATE TABLE ged.role (id uuid PRIMARY KEY, tenant_id uuid);
CREATE TABLE ged.app_role (id uuid PRIMARY KEY, tenant_id uuid);
CREATE TABLE ged.user_role (user_id uuid, role_id uuid);
CREATE TABLE ged.permission (code text PRIMARY KEY, reg_status text);
CREATE TABLE ged.role_permission (role_id uuid, tenant_id uuid, permission_code text, reg_status text);
CREATE TABLE ged.document (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    code text,
    title text,
    description text,
    is_confidential boolean NOT NULL DEFAULT false,
    status ged.document_status_enum NOT NULL DEFAULT 'ACTIVE',
    reg_status text NOT NULL DEFAULT 'A',
    created_at timestamptz NOT NULL DEFAULT now(),
    current_version_id uuid,
    folder_id uuid
);
CREATE TABLE ged.document_version (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL,
    version_number int NOT NULL DEFAULT 1,
    file_name text,
    content_type text,
    file_size_bytes bigint DEFAULT 0,
    is_partial_document boolean DEFAULT false,
    partial_status text DEFAULT 'NOT_PARTIAL',
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE ged.document_search (
    tenant_id uuid NOT NULL,
    document_id uuid NOT NULL PRIMARY KEY,
    version_id uuid,
    file_name text,
    ocr_text text,
    search_vector tsvector
);
CREATE TABLE ged.folder (id uuid PRIMARY KEY, tenant_id uuid, name text);
CREATE TABLE ged.ocr_job (id uuid PRIMARY KEY, tenant_id uuid, document_version_id uuid, status text, requested_at timestamptz);
CREATE TABLE ged.document_acl (document_id uuid, user_id uuid, role_id uuid, can_read boolean, can_write boolean);

CREATE INDEX idx_doc_tenant_reg ON ged.document (tenant_id, reg_status);
CREATE INDEX idx_doc_confidential ON ged.document (is_confidential);
""");

            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();

            await conn.ExecuteAsync("""
INSERT INTO ged.tenant VALUES (@tenantId, 'Tenant Benchmark', 'bench');
INSERT INTO ged.app_user VALUES (@userId, @tenantId, 'A', true);
INSERT INTO ged.role VALUES (@roleId, @tenantId);
INSERT INTO ged.app_role VALUES (@roleId, @tenantId);
INSERT INTO ged.user_role VALUES (@userId, @roleId);
INSERT INTO ged.permission VALUES ('Documents.View', 'A'), ('GED.DOCUMENTS', 'A');
INSERT INTO ged.role_permission VALUES (@roleId, @tenantId, 'Documents.View', 'A');
""", new { tenantId, userId, roleId });

            // 2. Insert 2,000 synthetic documents
            const int docCount = 2000;
            var docIds = new List<Guid>(docCount);
            for (var i = 0; i < docCount; i++)
            {
                docIds.Add(Guid.NewGuid());
            }

            await conn.ExecuteAsync("""
INSERT INTO ged.document (id, tenant_id, code, title, description, is_confidential, status, reg_status, created_at)
SELECT 
    d_id,
    @tenantId,
    'DOC-' || row_number() over(),
    'Documento Sintetico ' || row_number() over(),
    'Descricao de teste para busca autorizada benchmark',
    (row_number() over() % 10 = 0), -- 10% confidenciais
    'ACTIVE'::ged.document_status_enum,
    'A',
    now() - (row_number() over() || ' minutes')::interval
FROM unnest(@ids) AS d_id;
""", new { tenantId, ids = docIds.ToArray() });

            // Give ACL to user for 50 of the 200 confidential documents
            await conn.ExecuteAsync("""
INSERT INTO ged.document_acl (document_id, user_id, can_read, can_write)
SELECT id, @userId, true, false
FROM ged.document
WHERE tenant_id=@tenantId AND is_confidential=true
LIMIT 50;
""", new { tenantId, userId });

            // Search query templates
            const string searchSqlTemplate = """
WITH base AS (
SELECT d.id AS "DocumentId",
COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) AS "VersionId",
COALESCE(NULLIF(d.code,''),d.id::text) AS "Code", COALESCE(NULLIF(d.title,''),'Documento sem título') AS "Title",
COALESCE(NULLIF(s.file_name,''),NULLIF(v.file_name,''),NULLIF(latest_v.file_name,''),'arquivo') AS "FileName",
COALESCE(NULLIF(v.content_type,''),NULLIF(latest_v.content_type,''),'') AS "ContentType",
COALESCE(v.file_size_bytes, latest_v.file_size_bytes,0) AS "SizeBytes", d.created_at AS "CreatedAt", NULLIF(COALESCE(s.ocr_text,''),'') IS NOT NULL AS "HasOcrText",
f.name AS "FolderName", NULL::text AS "FolderPath", COALESCE(oj.status::text,'NONE') AS "OcrStatus",
COALESCE(v.is_partial_document, latest_v.is_partial_document, false) AS "IsPartialDocument",
(upper(COALESCE(v.partial_status, latest_v.partial_status, 'NOT_PARTIAL')) = 'INCOMPLETE') AS "IsDocumentIncomplete",
COALESCE(v.partial_status, latest_v.partial_status, 'NOT_PARTIAL') AS "PartialStatus",
'TITLE' AS "MatchSource",
COALESCE(NULLIF(d.description,''),'Documento encontrado.') AS "Snippet",
85 AS "MatchScore",
0::double precision AS "Rank"
FROM ged.document d
LEFT JOIN ged.document_search s ON s.tenant_id=d.tenant_id AND s.document_id=d.id
LEFT JOIN ged.document_version v ON v.tenant_id=d.tenant_id AND v.id=s.version_id
LEFT JOIN ged.folder f ON f.tenant_id=d.tenant_id AND f.id=d.folder_id
LEFT JOIN LATERAL (SELECT vx.id, vx.file_name, vx.content_type, vx.file_size_bytes, vx.is_partial_document, vx.partial_status FROM ged.document_version vx WHERE vx.tenant_id=d.tenant_id AND vx.document_id=d.id ORDER BY vx.version_number DESC, vx.created_at DESC LIMIT 1) latest_v ON true
LEFT JOIN LATERAL (SELECT j.status FROM ged.ocr_job j WHERE j.tenant_id=d.tenant_id AND j.document_version_id=COALESCE(NULLIF(s.version_id,'00000000-0000-0000-0000-000000000000'::uuid),NULLIF(d.current_version_id,'00000000-0000-0000-0000-000000000000'::uuid),latest_v.id) ORDER BY j.requested_at DESC LIMIT 1) oj ON true
WHERE d.tenant_id=@tenantId AND coalesce(d.reg_status,'A')='A' AND d.status<>'ARCHIVED'::ged.document_status_enum
AND /*AUTH_FILTER*/
AND (d.code ILIKE @likeQuery OR d.title ILIKE @likeQuery OR COALESCE(d.description,'') ILIKE @likeQuery)
), filtered AS (
SELECT * FROM base
)
SELECT filtered.*, agg."TotalRows", agg."TotalWithOcr", agg."TotalWithoutOcr", '{}' AS "TotalByType"
FROM filtered
CROSS JOIN LATERAL (SELECT count(*)::int AS "TotalRows", count(*) FILTER (WHERE upper(COALESCE("OcrStatus",''))='COMPLETED' AND "HasOcrText")::int AS "TotalWithOcr", count(*) FILTER (WHERE NOT (upper(COALESCE("OcrStatus",''))='COMPLETED' AND "HasOcrText"))::int AS "TotalWithoutOcr" FROM filtered) agg
ORDER BY "CreatedAt" DESC
LIMIT 20 OFFSET 0;
""";

            // 3. Baseline Measurement: Load all IDs into memory, then pass array
            var factory = new NpgsqlConnectionFactory(dsn);
            var abacService = new AbacAuthorizationService(factory);

            // Warm-up
            await conn.ExecuteAsync("SELECT 1");

            var baselineAllocBefore = GC.GetTotalAllocatedBytes(true);
            var baselineSw = Stopwatch.StartNew();

            // Step 1 of baseline: Load all active doc IDs
            var candidateIds = (await conn.QueryAsync<Guid>("SELECT id FROM ged.document WHERE tenant_id=@tenantId AND reg_status='A'", new { tenantId })).ToArray();
            var authorizedIds = (await abacService.FilterDocumentsAsync(tenantId, userId, candidateIds, "VIEW", default)).ToArray();

            // Step 2 of baseline: Search using ANY(@authorizedIds)
            var baselineSql = searchSqlTemplate.Replace("/*AUTH_FILTER*/", "d.id = ANY(@authorizedIds)");
            var baselineRows = (await conn.QueryAsync(baselineSql, new { tenantId, authorizedIds, likeQuery = "%Sintetico%" })).ToList();
            baselineSw.Stop();
            var baselineAllocAfter = GC.GetTotalAllocatedBytes(true);
            var baselineAllocatedBytes = baselineAllocAfter - baselineAllocBefore;

            // 4. Pushdown Predicate Measurement: Single query with DocumentAccessPredicate
            var pushdownAllocBefore = GC.GetTotalAllocatedBytes(true);
            var pushdownSw = Stopwatch.StartNew();

            var pushdownSql = searchSqlTemplate.Replace("/*AUTH_FILTER*/", AbacAuthorizationService.DocumentAccessPredicate("d"));
            var pushdownRows = (await conn.QueryAsync(pushdownSql, new {
                tenantId,
                userId,
                permissionCode = "Documents.View",
                write = false,
                hour = DateTime.UtcNow.Hour,
                likeQuery = "%Sintetico%"
            })).ToList();
            pushdownSw.Stop();
            var pushdownAllocAfter = GC.GetTotalAllocatedBytes(true);
            var pushdownAllocatedBytes = pushdownAllocAfter - pushdownAllocBefore;

            // 5. Query execution plans (EXPLAIN ANALYZE)
            var explainBaseline = string.Join("\n", await conn.QueryAsync<string>("EXPLAIN ANALYZE " + baselineSql, new { tenantId, authorizedIds, likeQuery = "%Sintetico%" }));
            var explainPushdown = string.Join("\n", await conn.QueryAsync<string>("EXPLAIN ANALYZE " + pushdownSql, new {
                tenantId,
                userId,
                permissionCode = "Documents.View",
                write = false,
                hour = DateTime.UtcNow.Hour,
                likeQuery = "%Sintetico%"
            }));

            // Verification
            Assert.Equal(baselineRows.Count, pushdownRows.Count);
            Assert.True(pushdownRows.Count > 0);

            // Log detailed results
            output.WriteLine($"=== BENCHMARK RESULT ({docCount} Documentos) ===");
            output.WriteLine($"Baseline (2 queries + array 2000 IDs em memoria): {baselineSw.ElapsedMilliseconds} ms, {baselineAllocatedBytes:N0} bytes alocados, {baselineRows.Count} itens");
            output.WriteLine($"Pushdown (1 query direta com predicado SQL):        {pushdownSw.ElapsedMilliseconds} ms, {pushdownAllocatedBytes:N0} bytes alocados, {pushdownRows.Count} itens");
            output.WriteLine($"Reducao de Memoria: {((double)(baselineAllocatedBytes - pushdownAllocatedBytes) / baselineAllocatedBytes * 100):F1}%");
            output.WriteLine($"\n--- EXPLAIN BASELINE ---\n{explainBaseline.Substring(0, Math.Min(400, explainBaseline.Length))}...");
            output.WriteLine($"\n--- EXPLAIN PUSHDOWN ---\n{explainPushdown.Substring(0, Math.Min(400, explainPushdown.Length))}...");

            // Assert pushdown is significantly more memory-efficient
            Assert.True(pushdownAllocatedBytes < baselineAllocatedBytes, "Pushdown deve alocar menos memoria do que carregar todos os IDs para a aplicacao.");
        }
        finally
        {
            await admin.ExecuteAsync($"drop database {dbName}");
        }
    }
}
