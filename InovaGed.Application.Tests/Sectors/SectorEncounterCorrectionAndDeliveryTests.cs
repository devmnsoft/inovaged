using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Audit;
using InovaGed.Application.Common;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Security;
using InovaGed.Application.Sectors;
using InovaGed.Application.Storage;
using InovaGed.Application.Tests.Ged;
using InovaGed.Domain.Primitives;
using InovaGed.Infrastructure.ArtificialIntelligence;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Sectors;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text.Json;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorEncounterCorrectionAndDeliveryTests
{
    [LocalPgFact]
    public async Task Link_correction_moves_encounter_transactionally_isolating_contexts_and_handling_suggestions_and_findings()
    {
        var name = "ig_corr_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await RunMigratorInstall(builder.ConnectionString);

            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();

            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var encounter1 = Guid.NewGuid();
            var encounter2 = Guid.NewGuid();
            var document = Guid.NewGuid();
            var version = Guid.NewGuid();
            var link = Guid.NewGuid();
            var role = Guid.NewGuid();

            var fixtureArgs = new { tenant, user, patient, encounter1, encounter2, document, version, link, role };

            // Setup tenant, user, patient, encounters, document, version, and link
            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Teste','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuário Teste','user@hospital.test','hash');
insert into ged.role(id,tenant_id,code,name) values(@role,@tenant,'HOSPITAL','Hospital');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Hospital','HOSPITAL');
insert into ged.user_role(user_id,role_id) values(@user,@role);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant,@role,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant,@user,'FATURAMENTO','ADMINISTRAR');

insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient,@tenant,'MV','1','000120','Paciente Alfa','ID',now());

insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter1,@tenant,@patient,'ENC-001',now());

insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter2,@tenant,@patient,'ENC-002',now());

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@document,@tenant,'DOC-001','Prescrição Médica',false,@user);

insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,content_text,created_by)
values(@version,@tenant,@document,1,'prescricao.pdf','.pdf',1024,'prescricao.pdf',repeat('a',64),'application/pdf','Dipirona 500 mg via oral',@user);

insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values(@link,@tenant,@patient,@encounter1,@document,@version,'DS23','GED_UPLOAD','prescricao.pdf',repeat('a',64),@user,'GRAVADO','PRONTO','CONCLUIDO','REVISAO_PENDENTE');
""", fixtureArgs);

            // Add unreviewed suggestion (SUGERIDO)
            var unreviewedField = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_extracted_field(id,tenant_id,link_id,version_id,mechanism,source_kind,field_key,suggested_value,evidence_excerpt,status,updated_by,context_code)
values(@unreviewedField,@tenant,@link,@version,'REGRAS_LOCAIS_V1','TEXTO_NATIVO','medicamento','Dipirona 500 mg','Dipirona 500 mg via oral','SUGERIDO',@user,'PRESCRITO');
""", new { unreviewedField, tenant, link, version, user });

            // Add reviewed / confirmed suggestion (CONFIRMADO)
            var confirmedField = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_extracted_field(id,tenant_id,link_id,version_id,mechanism,source_kind,field_key,suggested_value,confirmed_value,evidence_excerpt,status,updated_by,context_code)
values(@confirmedField,@tenant,@link,@version,'REGRAS_LOCAIS_V1','TEXTO_NATIVO','dose','500 mg','500 mg conferido','Dipirona 500 mg via oral','CONFIRMADO',@user,'PRESCRITO');
insert into ged.sector_field_review(id,tenant_id,field_id,to_status,to_value,actor_id)
values(gen_random_uuid(),@tenant,@confirmedField,'CONFIRMADO','500 mg conferido',@user);
""", new { confirmedField, tenant, link, version, user });

            // Add finding on Encounter 1
            var findingId = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_finding(id,tenant_id,encounter_id,link_id,category,description,evidence_excerpt,review_status)
values(@findingId,@tenant,@encounter1,@link,'medicamento','Dipirona 500 mg','Dipirona 500 mg via oral','ABERTO');
""", new { findingId, tenant, encounter1, link });

            var factory = new LocalDbFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var service = new DemaisSetoresService(factory, null!, null!, null!, null!, null!, auth, new TestAuditWriter(), Options.Create(new DocumentUploadOptions()), new UnconfiguredClinicalSource(), NullLogger<DemaisSetoresService>.Instance);
            await service.EnsureTenantAsync(tenant, CancellationToken.None);

            // Execute canonical correction: Move Link from encounter1 to encounter2
            var correctionRequest = new SectorCorrectionRequest(
                tenant, user, false, link, encounter2, "Correção de atendimento de paciente", "127.0.0.1", "TestAgent",
                ToPatientId: patient, ToDocumentId: document);

            var error = await service.CorrectLinkAsync(correctionRequest, CancellationToken.None);
            Assert.Null(error);

            // 1. Link is updated to encounter2 and extraction scheduled (ENFILEIRADO)
            var linkEncounter = await db.ExecuteScalarAsync<Guid>("select encounter_id from ged.sector_document_link where id=@link", fixtureArgs);
            var extractionStatus = await db.ExecuteScalarAsync<string>("select extraction_status from ged.sector_document_link where id=@link", fixtureArgs);
            Assert.Equal(encounter2, linkEncounter);
            Assert.Equal("ENFILEIRADO", extractionStatus);

            // 2. Audit record in ged.sector_link_correction is present with correct metadata
            var correctionRecord = await db.QuerySingleOrDefaultAsync("""
select from_encounter_id, to_encounter_id, reason, actor_id, from_patient_id, to_patient_id
from ged.sector_link_correction
where link_id=@link and tenant_id=@tenant
""", fixtureArgs);
            Assert.NotNull(correctionRecord);
            Assert.Equal(encounter1, (Guid)correctionRecord.from_encounter_id);
            Assert.Equal(encounter2, (Guid)correctionRecord.to_encounter_id);
            Assert.Equal("Correção de atendimento de paciente", (string)correctionRecord.reason);
            Assert.Equal(user, (Guid)correctionRecord.actor_id);

            // 3. Unreviewed suggestion was marked SUPERADO
            var unreviewedStatus = await db.ExecuteScalarAsync<string>("select status from ged.sector_extracted_field where id=@unreviewedField", new { unreviewedField });
            Assert.Equal("SUPERADO", unreviewedStatus);

            // 4. Confirmed suggestion was preserved as REVISAO_NECESSARIA
            var confirmedStatus = await db.ExecuteScalarAsync<string>("select status from ged.sector_extracted_field where id=@confirmedField", new { confirmedField });
            Assert.Equal("REVISAO_NECESSARIA", confirmedStatus);

            // Historical field reviews must be preserved
            var reviewCount = await db.ExecuteScalarAsync<int>("select count(*) from ged.sector_field_review where field_id=@confirmedField", new { confirmedField });
            Assert.True(reviewCount >= 1);

            // 5. Findings associated with the link are updated to encounter2
            var findingEncounter = await db.ExecuteScalarAsync<Guid>("select encounter_id from ged.sector_finding where id=@findingId", new { findingId });
            Assert.Equal(encounter2, findingEncounter);

            // 6. Context 1 has ZERO active suggestions or findings
            var activeInEncounter1 = await db.ExecuteScalarAsync<int>("""
select count(*) from ged.sector_extracted_field f
join ged.sector_document_link l on l.id=f.link_id
where l.encounter_id=@encounter1 and f.status in ('SUGERIDO', 'CONFIRMADO')
""", fixtureArgs);
            Assert.Equal(0, activeInEncounter1);

            var activeFindingsInEncounter1 = await db.ExecuteScalarAsync<int>("""
select count(*) from ged.sector_finding where encounter_id=@encounter1 and review_status='ABERTO'
""", fixtureArgs);
            Assert.Equal(0, activeFindingsInEncounter1);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Delivery_persists_order_catalog_code_blockers_and_confirms_with_evidence()
    {
        var name = "ig_deliv_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await RunMigratorInstall(builder.ConnectionString);

            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();

            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var encounter = Guid.NewGuid();
            var doc1 = Guid.NewGuid();
            var ver1 = Guid.NewGuid();
            var link1 = Guid.NewGuid();
            var role = Guid.NewGuid();

            var fixtureArgs = new { tenant, user, patient, encounter, doc1, ver1, link1, role };

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Teste Entrega','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuário Entrega','delivery@hospital.test','hash');
insert into ged.role(id,tenant_id,code,name) values(@role,@tenant,'HOSPITAL','Hospital');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Hospital','HOSPITAL');
insert into ged.user_role(user_id,role_id) values(@user,@role);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant,@role,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant,@user,'FATURAMENTO','ADMINISTRAR');

insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient,@tenant,'MV','1','000120','Paciente Alfa','ID',now());

insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter,@tenant,@patient,'ENC-001',now());

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@doc1,@tenant,'DOC-001','Ficha de Atendimento',false,@user);

insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver1,@tenant,@doc1,1,'ficha.pdf','.pdf',1024,'ficha.pdf',repeat('d',64),'application/pdf',@user);

insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status,review_status)
values(@link1,@tenant,@patient,@encounter,@doc1,@ver1,'DS01','GED_UPLOAD','ficha.pdf',repeat('d',64),@user,'GRAVADO','PRONTO','CONCLUIDO','CONCLUIDO','CONFERIDO');
""", fixtureArgs);

            var factory = new LocalDbFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var service = new DemaisSetoresService(factory, null!, null!, null!, null!, null!, auth, new TestAuditWriter(), Options.Create(new DocumentUploadOptions()), new UnconfiguredClinicalSource(), NullLogger<DemaisSetoresService>.Instance);
            await service.EnsureTenantAsync(tenant, CancellationToken.None);

            // 1. Prepare Delivery
            var deliveryReq = new SectorDeliveryRequest(
                tenant, user, false, encounter, "ANEXO_I", "Faturamento Central",
                [link1],
                "127.0.0.1", "TestAgent");

            var deliveryResult = await service.PrepareDeliveryAsync(deliveryReq, CancellationToken.None);
            Assert.True(deliveryResult.Recorded);
            Assert.NotNull(deliveryResult.DeliveryId);
            var deliveryId = deliveryResult.DeliveryId.Value;

            // 2. Fetch Delivery and verify persisted items and order
            var details = await service.GetDeliveryAsync(tenant, user, false, deliveryId, CancellationToken.None);
            Assert.NotNull(details);
            Assert.Equal("ANEXO_I", details.ProfileCode);
            Assert.Equal("Faturamento Central", details.Recipient);
            Assert.Equal("PREPARADO", details.Status);
            Assert.Single(details.Items);
            Assert.Equal("DS01", details.Items[0].CatalogCode);
            Assert.Equal(1, details.Items[0].ItemOrder);

            // 3. Confirm Delivery with Protocol and Evidence
            var confirmErr = await service.ConfirmDeliveryAsync(
                tenant, user, false, deliveryId, "PROT-2026-1015", "Assinatura física arquivada na caixa 42",
                "127.0.0.1", "TestAgent", CancellationToken.None);
            Assert.Null(confirmErr);

            // 4. Verify updated delivery state
            var confirmed = await service.GetDeliveryAsync(tenant, user, false, deliveryId, CancellationToken.None);
            Assert.NotNull(confirmed);
            Assert.Equal("ENTREGUE", confirmed.Status);
            Assert.Equal("PROT-2026-1015", confirmed.ProtocolNumber);
            Assert.Equal("Assinatura física arquivada na caixa 42", confirmed.DeliveryEvidence);
            Assert.NotNull(confirmed.DeliveredAt);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Storage_orphan_detector_reports_unreferenced_files_outside_grace_period_without_touching_active_uploads()
    {
        var name = "ig_orph_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");

        var tempStorage = Path.Combine(Path.GetTempPath(), "inovaged_orphan_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStorage);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await RunMigratorInstall(builder.ConnectionString);

            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();

            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            var doc = Guid.NewGuid();
            var ver = Guid.NewGuid();

            // 1. Create a confirmed version file and register in database
            var confirmedFile = Path.Combine(tempStorage, "confirmed_v1.pdf");
            await File.WriteAllTextAsync(confirmedFile, "confirmed content");

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Teste Orfao','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuário Órfão','orph@hospital.test','hash');
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(@doc,@tenant,'DOC-CONF','Doc',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver,@tenant,@doc,1,'confirmed_v1.pdf','.pdf',100,'confirmed_v1.pdf',repeat('c',64),'application/pdf',@user);
""", new { tenant, user, doc, ver });

            // 2. Create an orphan file older than grace period (3 days old)
            var orphanFile = Path.Combine(tempStorage, "orphan_old.pdf");
            await File.WriteAllTextAsync(orphanFile, "orphan content");
            File.SetLastWriteTimeUtc(orphanFile, DateTime.UtcNow.AddDays(-3));

            // 3. Create a recent file within grace period (1 hour old)
            var recentFile = Path.Combine(tempStorage, "recent_unreferenced.pdf");
            await File.WriteAllTextAsync(recentFile, "recent content");
            File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddHours(-1));

            var factory = new LocalDbFactory(builder.ConnectionString);
            var detector = new StorageOrphanDetectionService(
                factory,
                Options.Create(new LocalStorageOptions { RootPath = tempStorage }),
                NullLogger<StorageOrphanDetectionService>.Instance);

            // Scan with 24h grace period
            var report = await detector.ScanOrphansAsync(tenant, TimeSpan.FromHours(24), CancellationToken.None);

            Assert.Equal(3, report.TotalFilesScanned);
            Assert.Equal(1, report.OrphanCandidatesFound);
            Assert.Contains(report.Candidates, c => c.RelativePath.Contains("orphan_old.pdf"));
            Assert.DoesNotContain(report.Candidates, c => c.RelativePath.Contains("confirmed_v1.pdf"));
            Assert.DoesNotContain(report.Candidates, c => c.RelativePath.Contains("recent_unreferenced.pdf"));

            // Verify dry-run safety: orphan file was NOT deleted
            Assert.True(File.Exists(orphanFile));
            Assert.True(File.Exists(recentFile));
            Assert.True(File.Exists(confirmedFile));
        }
        finally
        {
            if (Directory.Exists(tempStorage))
                Directory.Delete(tempStorage, true);

            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    private static async Task RunMigratorInstall(string dsn)
    {
        var workspace = FindRoot();
        var migrator = Path.Combine(workspace, "InovaGed.Database.Migrator", "bin", "Debug", "net8.0", "InovaGed.Database.Migrator.dll");
        if (!File.Exists(migrator))
        {
            migrator = Path.Combine(workspace, "InovaGed.Database.Migrator", "InovaGed.Database.Migrator.csproj");
        }
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(migrator);
        start.ArgumentList.Add("install");
        start.ArgumentList.Add("--verify");
        start.Environment["ConnectionStrings__DefaultConnection"] = dsn;

        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"Migrator failed with exit code {process.ExitCode}.\nStdout: {stdout}\nStderr: {stderr}");
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Could not find InovaGed.sln root.");
    }

    private sealed class LocalDbFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;
        public LocalDbFactory(string connectionString) => _connectionString = connectionString;
        public System.Data.IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
        public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
        {
            var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }
    }

    private sealed class TestAuditWriter : IAuditWriter
    {
        public Task<Result> WriteAsync(AuditWriteCommand command, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct)
            => Task.FromResult(Result.Ok());
    }
}
