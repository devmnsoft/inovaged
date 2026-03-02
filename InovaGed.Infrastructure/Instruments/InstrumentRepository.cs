using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using InovaGed.Application.Audit;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Instruments;
using Microsoft.Extensions.Logging;

namespace InovaGed.Infrastructure.Instruments
{
    public sealed class InstrumentRepository : IInstrumentRepository
    {
        private readonly IDbConnectionFactory _db;
        private readonly ILogger<InstrumentRepository> _logger;
        private readonly IAuditWriter _audit;

        public InstrumentRepository(IDbConnectionFactory db, ILogger<InstrumentRepository> logger, IAuditWriter audit)
        {
            _db = db; _logger = logger; _audit = audit;
        }

        public async Task<IReadOnlyList<InstrumentVersionRow>> ListVersionsAsync(Guid tenantId, string type, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);
                var sql = """
            SELECT id, instrument_type::text AS instrumenttype, version_no AS versionno,
                   published_at AS publishedat, COALESCE(published_by_name,'') AS publishedbyname,
                   hash_sha256 AS hashsha256, notes
            FROM ged.instrument_version
            WHERE tenant_id=@TenantId AND instrument_type=@Type::ged.instrument_type AND reg_status='A'
            ORDER BY version_no DESC;
            """;
                var rows = await con.QueryAsync<InstrumentVersionRow>(new CommandDefinition(sql, new { TenantId = tenantId, Type = type }, cancellationToken: ct));
                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListVersionsAsync failed Tenant={Tenant} Type={Type}", tenantId, type);
                throw;
            }
        }

        public async Task<Guid> PublishNewVersionAsync(Guid tenantId, Guid? userId, string? userName, string type, string? notes, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);

                var next = await con.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COALESCE(MAX(version_no),0)+1
                FROM ged.instrument_version
                WHERE tenant_id=@TenantId AND instrument_type=@Type::ged.instrument_type AND reg_status='A';
            """, new { TenantId = tenantId, Type = type }, cancellationToken: ct));

                // hash: na PoC basta hash da "fotografia" da versão (nodes)
                var seed = $"{tenantId}|{type}|{next}|{DateTimeOffset.UtcNow:O}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

                var id = Guid.NewGuid();

                await con.ExecuteAsync(new CommandDefinition("""
                INSERT INTO ged.instrument_version(id, tenant_id, instrument_type, version_no, published_at, published_by, published_by_name, notes, hash_sha256)
                VALUES (@Id, @TenantId, @Type::ged.instrument_type, @VersionNo, now(), @UserId, @UserName, @Notes, @Hash);
            """, new { Id = id, TenantId = tenantId, Type = type, VersionNo = next, UserId = userId, UserName = userName, Notes = notes, Hash = hash }, cancellationToken: ct));

                // opcional: copiar nodes da última versão para nova (para facilitar edição)
                var lastId = await con.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT id
                FROM ged.instrument_version
                WHERE tenant_id=@TenantId AND instrument_type=@Type::ged.instrument_type AND version_no=@Prev AND reg_status='A'
                LIMIT 1;
            """, new { TenantId = tenantId, Type = type, Prev = next - 1 }, cancellationToken: ct));

                if (lastId.HasValue)
                {
                    // Copia mantendo ids novos e preservando hierarquia por mapeamento
                    var nodes = (await con.QueryAsync<InstrumentNodeRow>(new CommandDefinition("""
                    SELECT id, parent_id AS parentid, code, title, description, sort_order AS sortorder, security_level::text AS securitylevel
                    FROM ged.instrument_node
                    WHERE tenant_id=@TenantId AND instrument_type=@Type::ged.instrument_type AND version_id=@Ver AND reg_status='A'
                    ORDER BY sort_order;
                """, new { TenantId = tenantId, Type = type, Ver = lastId.Value }, cancellationToken: ct))).ToList();

                    var map = new Dictionary<Guid, Guid>();
                    foreach (var n in nodes) map[n.Id] = Guid.NewGuid();

                    foreach (var n in nodes)
                    {
                        await con.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO ged.instrument_node(id, tenant_id, instrument_type, version_id, parent_id, code, title, description, sort_order, security_level)
                        VALUES (@Id, @TenantId, @Type::ged.instrument_type, @Ver, @Parent, @Code, @Title, @Desc, @Sort, @Sec::ged.security_level);
                    """, new
                        {
                            Id = map[n.Id],
                            TenantId = tenantId,
                            Type = type,
                            Ver = id,
                            Parent = n.ParentId.HasValue ? map[n.ParentId.Value] : (Guid?)null,
                            Code = n.Code,
                            Title = n.Title,
                            Desc = n.Description,
                            Sort = n.SortOrder,
                            Sec = n.SecurityLevel
                        }, cancellationToken: ct));
                    }
                }

                await _audit.WriteAsync(tenantId, userId, userName, "Instrument.Publish", "InstrumentVersion", id, true,
                    new { type, version = next }, null, null, ct);

                return id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PublishNewVersionAsync failed Tenant={Tenant} Type={Type}", tenantId, type);
                throw;
            }
        }

        public async Task<IReadOnlyList<InstrumentNodeRow>> ListNodesAsync(Guid tenantId, string type, Guid versionId, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);
                var sql = """
            SELECT id, parent_id AS parentid, code, title, description, sort_order AS sortorder, security_level::text AS securitylevel
            FROM ged.instrument_node
            WHERE tenant_id=@TenantId AND instrument_type=@Type::ged.instrument_type AND version_id=@Ver AND reg_status='A'
            ORDER BY sort_order;
            """;
                var rows = await con.QueryAsync<InstrumentNodeRow>(new CommandDefinition(sql, new { TenantId = tenantId, Type = type, Ver = versionId }, cancellationToken: ct));
                return rows.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListNodesAsync failed Tenant={Tenant} Type={Type} Ver={Ver}", tenantId, type, versionId);
                throw;
            }
        }

        public async Task<Guid> UpsertNodeAsync(Guid tenantId, Guid? userId, string? userName, string type, Guid versionId,
            Guid? id, Guid? parentId, string code, string title, string? desc, int sortOrder, string securityLevel, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);
                var nodeId = id ?? Guid.NewGuid();

                var sql = """
            INSERT INTO ged.instrument_node(id, tenant_id, instrument_type, version_id, parent_id, code, title, description, sort_order, security_level)
            VALUES (@Id, @TenantId, @Type::ged.instrument_type, @Ver, @ParentId, @Code, @Title, @Desc, @Sort, @Sec::ged.security_level)
            ON CONFLICT (id) DO UPDATE SET
              parent_id=EXCLUDED.parent_id,
              code=EXCLUDED.code,
              title=EXCLUDED.title,
              description=EXCLUDED.description,
              sort_order=EXCLUDED.sort_order,
              security_level=EXCLUDED.security_level;
            """;

                await con.ExecuteAsync(new CommandDefinition(sql, new
                {
                    Id = nodeId,
                    TenantId = tenantId,
                    Type = type,
                    Ver = versionId,
                    ParentId = parentId,
                    Code = code,
                    Title = title,
                    Desc = desc,
                    Sort = sortOrder,
                    Sec = securityLevel
                }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, userName, "Instrument.Node.Upsert", "InstrumentNode", nodeId, true,
                    new { type, versionId, parentId, code }, null, null, ct);

                return nodeId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpsertNodeAsync failed Tenant={Tenant} Type={Type}", tenantId, type);
                throw;
            }
        }

        public async Task MoveNodeAsync(Guid tenantId, Guid? userId, string? userName, string type, Guid versionId, Guid nodeId, Guid? newParentId, int newSortOrder, CancellationToken ct)
        {
            try
            {
                await using var con = await _db.OpenAsync(ct);

                await con.ExecuteAsync(new CommandDefinition("""
            UPDATE ged.instrument_node
            SET parent_id=@Parent, sort_order=@Sort
            WHERE tenant_id=@TenantId AND instrument_type=@Type::ged.instrument_type AND version_id=@Ver AND id=@Id AND reg_status='A';
            """, new { TenantId = tenantId, Type = type, Ver = versionId, Id = nodeId, Parent = newParentId, Sort = newSortOrder }, cancellationToken: ct));

                await _audit.WriteAsync(tenantId, userId, userName, "Instrument.Node.Move", "InstrumentNode", nodeId, true,
                    new { type, versionId, newParentId, newSortOrder }, null, null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoveNodeAsync failed Tenant={Tenant} Node={Node}", tenantId, nodeId);
                throw;
            }
        }

        public async Task<string> RenderPrintHtmlAsync(Guid tenantId, string type, Guid versionId, CancellationToken ct)
        {
            var nodes = await ListNodesAsync(tenantId, type, versionId, ct);

            // impressão simples e formal (PoC). Depois você pode estilizar melhor.
            var sb = new StringBuilder();
            sb.Append("""
        <html><head><meta charset="utf-8"/>
        <style>
          body{font-family:Arial; font-size:12px;}
          h1{font-size:18px;margin:0 0 10px 0;}
          .small{color:#666}
          ul{list-style:none;padding-left:0}
          li{margin:6px 0}
          .code{font-weight:bold; display:inline-block; width:90px}
        </style></head><body>
        """);
            sb.Append($"<h1>{type} - Versão {versionId}</h1>");
            sb.Append($"<div class='small'>Emitido em {DateTimeOffset.Now:dd/MM/yyyy HH:mm}</div><hr/>");

            // render árvore por parent
            var byParent = nodes.GroupBy(n => n.ParentId).ToDictionary(g => g.Key, g => g.OrderBy(x => x.SortOrder).ToList());

            void Render(Guid? parent, int depth)
            {
                if (!byParent.TryGetValue(parent, out var list)) return;
                sb.Append("<ul>");
                foreach (var n in list)
                {
                    sb.Append("<li>");
                    sb.Append(new string('&', 0)); // no-op
                    sb.Append($"<span class='code'>{System.Net.WebUtility.HtmlEncode(n.Code)}</span> {System.Net.WebUtility.HtmlEncode(n.Title)}");
                    if (!string.IsNullOrWhiteSpace(n.Description))
                        sb.Append($"<div class='small'>{System.Net.WebUtility.HtmlEncode(n.Description!)}</div>");
                    Render(n.Id, depth + 1);
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }

            Render(null, 0);

            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}