using Dapper;
using InovaGed.Application.ArtificialIntelligence;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Sectors;
using InovaGed.Application.Storage;
using InovaGed.Application.Tests.Ged;
using InovaGed.Infrastructure.ArtificialIntelligence;
using InovaGed.Infrastructure.Ged.Documents;
using InovaGed.Infrastructure.Security;
using InovaGed.Infrastructure.Sectors;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Text.Json;
using Xunit;

namespace InovaGed.Application.Tests.Sectors;

public sealed class SectorDeepStabilizationAndIntegrityTests
{
    [LocalPgFact]
    public async Task Segmented_extraction_with_three_segments_persists_durable_segments_handles_failure_and_resumes()
    {
        var name = "ig_seg_" + Guid.NewGuid().ToString("N");
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
            var doc = Guid.NewGuid();
            var version = Guid.NewGuid();
            var link = Guid.NewGuid();
            var role = Guid.NewGuid();

            // Texto com 3 páginas distintas que excedem o limite por segmento, forçando pelo menos 3 segmentos
            var pad1 = new string('x', 200);
            var pad2 = new string('y', 200);
            var pad3 = new string('z', 200);
            var page1 = $"--- página 1 ---\nPaciente estável no leito. {pad1} Prescrito Omeprazol 40 mg via oral em jejum.";
            var page2 = $"--- página 2 ---\nEvolução de enfermagem. {pad2} Prescrito Amoxicilina 500 mg via oral a cada 8 horas.";
            var page3 = $"--- página 3 ---\nPlano terapêutico diário. {pad3} Prescrito Dipirona 1000 mg via intravenosa em caso de dor.";
            var fullText = $"{page1}\n\n{page2}\n\n{page3}";

            var fixtureArgs = new { tenant, user, patient, encounter, doc, version, link, role, fullText };

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Teste Segmentos','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuário IA','ia@hospital.test','hash');
insert into ged.role(id,tenant_id,code,name) values(@role,@tenant,'HOSPITAL','Hospital');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Hospital','HOSPITAL');
insert into ged.user_role(user_id,role_id) values(@user,@role);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant,@role,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant,@user,'FATURAMENTO','ADMINISTRAR');

insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient,@tenant,'MV','1','000300','Paciente Segmentado','ID',now());

insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter,@tenant,@patient,'ENC-300',now());

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@doc,@tenant,'DOC-300','Prontuário Multissegmento',false,@user);

insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,content_text,created_by)
values(@version,@tenant,@doc,1,'multi.pdf','.pdf',3072,'multi.pdf',repeat('m',64),'application/pdf',@fullText,@user);

insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values(@link,@tenant,@patient,@encounter,@doc,@version,'DS23','GED_UPLOAD','multi.pdf',repeat('m',64),@user,'GRAVADO','PRONTO','CONCLUIDO','AGUARDANDO');
""", fixtureArgs);

            var factory = new LocalDbFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var sectors = new DemaisSetoresService(factory, null!, null!, null!, null!, null!, auth, new TestAuditWriter(), Options.Create(new DocumentUploadOptions()), new UnconfiguredClinicalSource(), NullLogger<DemaisSetoresService>.Instance);
            await sectors.EnsureTenantAsync(tenant, CancellationToken.None);
            var mockGateway = new SegmentedTestAiGateway();
            var service = new SectorClinicalExtractionService(factory, sectors, auth, mockGateway, mockGateway, Options.Create(new DocumentAiOptions { MaximumInputCharacters = 1250 }));

            // 1. Primeira chamada: o gateway falha propositalmente no 2º segmento
            mockGateway.FailOnSegmentIndex = 1; // índice 1 = 2º segmento

            var resultFail = await service.ExtractAsync(tenant, user, link, "req-seg-1", CancellationToken.None);
            Assert.Equal(422, resultFail.Status); // gateway failure on segment 2 -> 422 FALHOU
            Assert.Equal("FALHOU", resultFail.State);

            // Verificar persistência durável no banco após a falha
            var segmentsAfterFail = (await db.QueryAsync<(int Index, string Status, Guid? ExecId)>(
                "select segment_index as Index, status as Status, ai_execution_id as ExecId from ged.sector_clinical_extraction_segment where tenant_id=@tenant order by segment_index",
                new { tenant })).ToArray();

            Assert.Equal(3, segmentsAfterFail.Length);
            Assert.Equal("CONCLUIDO", segmentsAfterFail[0].Status);
            Assert.NotNull(segmentsAfterFail[0].ExecId);
            Assert.Equal("FALHOU", segmentsAfterFail[1].Status);
            Assert.Equal("PROCESSANDO", segmentsAfterFail[2].Status);

            var execIdSeg1 = segmentsAfterFail[0].ExecId;
            var initialGatewayCalls = mockGateway.TotalExecutions;
            Assert.Equal(2, initialGatewayCalls); // executou seg 0 e tentou seg 1

            // 2. Retomada da extração: o erro é resolvido
            mockGateway.FailOnSegmentIndex = null;

            var resultResume = await service.ExtractAsync(tenant, user, link, "req-seg-2", CancellationToken.None, resumeRunId: resultFail.RunId);
            Assert.Equal(200, resultResume.Status);
            Assert.True(resultResume.Items >= 3);

            // Verificar que o segmento 0 NÃO foi reexecutado na IA (reutilizou concluído)
            // Apenas os segmentos 1 e 2 foram executados
            Assert.Equal(4, mockGateway.TotalExecutions); // 2 da primeira tentativa + 2 da retomada

            // Todos os 3 segmentos devem estar concluídos
            var finalSegments = (await db.QueryAsync<(int Index, string Status, Guid? ExecId, int ItemsReturned)>(
                "select segment_index as Index, status as Status, ai_execution_id as ExecId, items_returned as ItemsReturned from ged.sector_clinical_extraction_segment where tenant_id=@tenant order by segment_index",
                new { tenant })).ToArray();

            Assert.All(finalSegments, s => Assert.Equal("CONCLUIDO", s.Status));
            Assert.Equal(execIdSeg1, finalSegments[0].ExecId); // Mesmo execId preservado para o seg 0
            Assert.NotEqual(finalSegments[0].ExecId, finalSegments[1].ExecId); // Cada segmento tem sua própria IA execution!
            Assert.NotEqual(finalSegments[1].ExecId, finalSegments[2].ExecId);

            // 3. Verificar que as sugestões apontam para o segmento correto
            var extractedFields = (await db.QueryAsync<(Guid Id, Guid? SegmentId, Guid? AiExecId, int? Page, string Evidence)>(
                "select id as Id, segment_id as SegmentId, ai_execution_id as AiExecId, page_number as Page, evidence_excerpt as Evidence from ged.sector_extracted_field where tenant_id=@tenant and link_id=@link",
                new { tenant, link })).ToArray();

            Assert.True(extractedFields.Length >= 3);
            Assert.All(extractedFields, f => Assert.NotNull(f.SegmentId));

            // Cada campo gravou o ai_execution_id do seu respectivo segmento
            var seg1Field = extractedFields.FirstOrDefault(f => f.Evidence.Contains("Omeprazol"));
            var seg2Field = extractedFields.FirstOrDefault(f => f.Evidence.Contains("Amoxicilina"));
            var seg3Field = extractedFields.FirstOrDefault(f => f.Evidence.Contains("Dipirona"));

            Assert.NotNull(seg1Field.Evidence);
            Assert.NotNull(seg2Field.Evidence);
            Assert.NotNull(seg3Field.Evidence);

            Assert.Equal(finalSegments[0].ExecId, seg1Field.AiExecId);
            Assert.Equal(finalSegments[1].ExecId, seg2Field.AiExecId);
            Assert.Equal(finalSegments[2].ExecId, seg3Field.AiExecId);

            // Páginas derivadas corretamente
            Assert.Equal(1, seg1Field.Page);
            Assert.Equal(2, seg2Field.Page);
            Assert.Equal(3, seg3Field.Page);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Policy_blocked_state_persists_without_sql_exception_and_refuses_invalid_states()
    {
        var name = "ig_pol_" + Guid.NewGuid().ToString("N");
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
            var link = Guid.NewGuid();
            var doc = Guid.NewGuid();
            var ver = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var encounter = Guid.NewGuid();
            var user = Guid.NewGuid();

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Politica','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuário','u@h.t','h');
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(@doc,@tenant,'D1','T1',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver,@tenant,@doc,1,'f.pdf','.pdf',100,'f.pdf',repeat('0',64),'application/pdf',@user);
insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient,@tenant,'MV','1','P1','P1','ID',now());
insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter,@tenant,@patient,'E1',now());
insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values(@link,@tenant,@patient,@encounter,@doc,@ver,'DS01','GED_UPLOAD','f.pdf',repeat('0',64),@user,'GRAVADO','PRONTO','CONCLUIDO','AGUARDANDO');
""", new { tenant, user, doc, ver, patient, encounter, link });

            // 1. Inserir BLOQUEADO_POLITICA na tabela sector_clinical_extraction deve ter sucesso sem erro SQL
            var extractionId = Guid.NewGuid();
            await db.ExecuteAsync("""
insert into ged.sector_clinical_extraction(id, tenant_id, user_id, link_id, version_id, request_key, input_hash, mechanism, status)
values(@extractionId, @tenant, @user, @link, @ver, 'req-pol-test', repeat('0',64), 'IA_CLINICA_V1', 'BLOQUEADO_POLITICA')
""", new { extractionId, tenant, user, link, ver });

            var statusRecorded = await db.ExecuteScalarAsync<string>(
                "select status from ged.sector_clinical_extraction where id=@extractionId", new { extractionId });
            Assert.Equal("BLOQUEADO_POLITICA", statusRecorded);

            // 2. Estado inválido deve ser recusado pela constraint do banco
            var invalidEx = await Assert.ThrowsAsync<PostgresException>(() => db.ExecuteAsync("""
insert into ged.sector_clinical_extraction(id, tenant_id, user_id, link_id, version_id, request_key, input_hash, mechanism, status)
values(gen_random_uuid(), @tenant, @user, @link, @ver, 'req-invalid', repeat('0',64), 'IA_CLINICA_V1', 'ESTADO_INEXISTENTE')
""", new { tenant, user, link, ver }));

            Assert.Equal("23514", invalidEx.SqlState); // check_violation
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Link_correction_validates_document_version_consistency_and_rejects_incompatible_swaps()
    {
        var name = "ig_cval_" + Guid.NewGuid().ToString("N");
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
            var docA = Guid.NewGuid();
            var verA = Guid.NewGuid();
            var docB = Guid.NewGuid();
            var verB = Guid.NewGuid();
            var link = Guid.NewGuid();
            var role = Guid.NewGuid();

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Teste Validacao','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuario','u@h.t','h');
insert into ged.role(id,tenant_id,code,name) values(@role,@tenant,'HOSPITAL','Hospital');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Hospital','HOSPITAL');
insert into ged.user_role(user_id,role_id) values(@user,@role);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant,@role,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant,@user,'FATURAMENTO','ADMINISTRAR');

insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient,@tenant,'MV','1','P1','P1','ID',now());
insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter1,@tenant,@patient,'E1',now());
insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter2,@tenant,@patient,'E2',now());

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(@docA,@tenant,'DOC-A','Doc A',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@verA,@tenant,@docA,1,'a.pdf','.pdf',100,'a.pdf',repeat('a',64),'application/pdf',@user);

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(@docB,@tenant,'DOC-B','Doc B',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@verB,@tenant,@docB,1,'b.pdf','.pdf',100,'b.pdf',repeat('b',64),'application/pdf',@user);

insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status)
values(@link,@tenant,@patient,@encounter1,@docA,@verA,'DS01','GED_UPLOAD','a.pdf',repeat('a',64),@user,'GRAVADO','PRONTO','CONCLUIDO','AGUARDANDO');
""", new { tenant, user, patient, encounter1, encounter2, docA, verA, docB, verB, link, role });

            var factory = new LocalDbFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var service = new DemaisSetoresService(factory, null!, null!, null!, null!, null!, auth, new TestAuditWriter(), Options.Create(new DocumentUploadOptions()), new UnconfiguredClinicalSource(), NullLogger<DemaisSetoresService>.Instance);
            await service.EnsureTenantAsync(tenant, CancellationToken.None);

            // Cenário 1: Trocar document_id para Doc B mas SEM passar ToVersionId (deixando versão de outro documento) -> deve recusar!
            var reqMissingVer = new SectorCorrectionRequest(
                tenant, user, false, link, encounter1, "Troca de documento sem versão", "127.0.0.1", "Agent",
                ToPatientId: patient, ToDocumentId: docB, ToVersionId: null);

            var errMissingVer = await service.CorrectLinkAsync(reqMissingVer, CancellationToken.None);
            Assert.Contains("obrigatório informar a versão de destino", errMissingVer);

            // Cenário 2: Trocar document_id para Doc B mas passar a versão do Doc A -> deve recusar incompatibilidade!
            var reqIncompatibleVer = new SectorCorrectionRequest(
                tenant, user, false, link, encounter1, "Troca de documento com versão de outro documento", "127.0.0.1", "Agent",
                ToPatientId: patient, ToDocumentId: docB, ToVersionId: verA);

            var errIncompatibleVer = await service.CorrectLinkAsync(reqIncompatibleVer, CancellationToken.None);
            Assert.Contains("não pertence ao documento de destino", errIncompatibleVer);

            // Cenário 3: Trocar para Doc B informando a versão correta do Doc B -> sucesso!
            var reqValid = new SectorCorrectionRequest(
                tenant, user, false, link, encounter2, "Troca autorizada com par válido", "127.0.0.1", "Agent",
                ToPatientId: patient, ToDocumentId: docB, ToVersionId: verB);

            var errValid = await service.CorrectLinkAsync(reqValid, CancellationToken.None);
            Assert.Null(errValid);

            var updatedRow = await db.QuerySingleAsync<(Guid DocId, Guid VerId, Guid EncId)>(
                "select document_id as DocId, version_id as VerId, encounter_id as EncId from ged.sector_document_link where id=@link", new { link });

            Assert.Equal(docB, updatedRow.DocId);
            Assert.Equal(verB, updatedRow.VerId);
            Assert.Equal(encounter2, updatedRow.EncId);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Storage_orphan_detection_strictly_isolates_tenants_and_never_leaks_other_tenant_files()
    {
        var name = "ig_sorph_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");

        var tempStorage = Path.Combine(Path.GetTempPath(), "inovaged_multi_tenant_storage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempStorage);

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await RunMigratorInstall(builder.ConnectionString);

            await using var db = new NpgsqlConnection(builder.ConnectionString);
            await db.OpenAsync();

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenantA,'Tenant A','ta'), (@tenantB,'Tenant B','tb');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@userA,@tenantA,'User A','a@a.t','h'), (@userB,@tenantB,'User B','b@b.t','h');
""", new { tenantA, tenantB, userA, userB });

            // Arquivo do Tenant A confirmado no banco
            var fileA = Path.Combine(tempStorage, "tenantA_doc.pdf");
            await File.WriteAllTextAsync(fileA, "conteudo A");

            // Arquivo do Tenant B confirmado no banco dele
            var fileB = Path.Combine(tempStorage, "tenantB_doc.pdf");
            await File.WriteAllTextAsync(fileB, "conteudo B");

            // Arquivo genuinamente órfão antigo (sem dono em nenhum tenant)
            var fileOrphan = Path.Combine(tempStorage, "genuine_orphan.pdf");
            await File.WriteAllTextAsync(fileOrphan, "órfão");
            File.SetLastWriteTimeUtc(fileOrphan, DateTime.UtcNow.AddDays(-5));

            await db.ExecuteAsync("""
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(gen_random_uuid(),@tenantA,'D-A','Doc A',false,@userA);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
select gen_random_uuid(),@tenantA,id,1,'tenantA_doc.pdf','.pdf',100,'tenantA_doc.pdf',repeat('a',64),'application/pdf',@userA from ged.document where tenant_id=@tenantA;

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by) values(gen_random_uuid(),@tenantB,'D-B','Doc B',false,@userB);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
select gen_random_uuid(),@tenantB,id,1,'tenantB_doc.pdf','.pdf',100,'tenantB_doc.pdf',repeat('b',64),'application/pdf',@userB from ged.document where tenant_id=@tenantB;
""", new { tenantA, tenantB, userA, userB });

            var factory = new LocalDbFactory(builder.ConnectionString);
            var detector = new StorageOrphanDetectionService(
                factory,
                Options.Create(new LocalStorageOptions { RootPath = tempStorage }),
                NullLogger<StorageOrphanDetectionService>.Instance);

            // Varredura para o Tenant A
            var reportA = await detector.ScanOrphansAsync(tenantA, TimeSpan.FromHours(24), CancellationToken.None);

            // 1. Arquivo do Tenant B NÃO pode aparecer como candidato a órfão no relatório do Tenant A!
            Assert.DoesNotContain(reportA.Candidates, c => c.RelativePath.Contains("tenantB_doc.pdf"));

            // 2. Arquivo do Tenant A (referenciado) não é órfão
            Assert.DoesNotContain(reportA.Candidates, c => c.RelativePath.Contains("tenantA_doc.pdf"));

            // 3. Arquivo sem referência global aparece como candidato
            Assert.Contains(reportA.Candidates, c => c.RelativePath.Contains("genuine_orphan.pdf"));
        }
        finally
        {
            if (Directory.Exists(tempStorage))
                Directory.Delete(tempStorage, true);

            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Delivery_authorization_resolves_documents_from_persisted_version_and_denies_entirely_on_revocation_or_other_tenant()
    {
        var name = "ig_auth_" + Guid.NewGuid().ToString("N");
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

            var tenant1 = Guid.NewGuid();
            var tenant2 = Guid.NewGuid();
            var user1 = Guid.NewGuid();
            var role1 = Guid.NewGuid();
            var patient1 = Guid.NewGuid();
            var encounter1 = Guid.NewGuid();

            var doc1 = Guid.NewGuid();
            var ver1 = Guid.NewGuid();
            var link1 = Guid.NewGuid();

            var doc2 = Guid.NewGuid();
            var ver2 = Guid.NewGuid();
            var link2 = Guid.NewGuid();

            var doc3 = Guid.NewGuid();
            var ver3 = Guid.NewGuid();

            // Setup Tenant 1 e Tenant 2
            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant1,'Hospital Tenant 1','t1');
insert into ged.tenant(id,name,code) values(@tenant2,'Hospital Tenant 2','t2');

insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user1,@tenant1,'Dr. Autorizado','user1@hospital.test','hash');
insert into ged.role(id,tenant_id,code,name) values(@role1,@tenant1,'AUDITOR','Auditor');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role1,@tenant1,'Auditor','AUDITOR');
insert into ged.user_role(user_id,role_id) values(@user1,@role1);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant1,@role1,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant1,@user1,'FATURAMENTO','ADMINISTRAR');

insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient1,@tenant1,'MV','SADT','P001','Paciente Entrega','prontuario',now());

insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,confirmed_at)
values(@encounter1,@tenant1,@patient1,'ATD-001',now());

-- Documento 1
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@doc1,@tenant1,'DOC-001','Prescricao Medica',false,@user1);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver1,@tenant1,@doc1,1,'presc.pdf','.pdf',1000,'presc.pdf',repeat('1',64),'application/pdf',@user1);

insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status,review_status)
values(@link1,@tenant1,@patient1,@encounter1,@doc1,@ver1,'DS01','GED_UPLOAD','presc.pdf',repeat('1',64),@user1,'GRAVADO','PRONTO','CONCLUIDO','CONCLUIDO','CONFERIDO');

-- Documento 2
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@doc2,@tenant1,'DOC-002','Exame Laboratorial',false,@user1);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver2,@tenant1,@doc2,1,'exame.pdf','.pdf',1200,'exame.pdf',repeat('2',64),'application/pdf',@user1);

insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status,review_status)
values(@link2,@tenant1,@patient1,@encounter1,@doc2,@ver2,'DS02','GED_UPLOAD','exame.pdf',repeat('2',64),@user1,'GRAVADO','PRONTO','CONCLUIDO','CONCLUIDO','CONFERIDO');

-- Documento 3 (usado depois na correção de vínculo)
insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@doc3,@tenant1,'DOC-003','Relatorio Sigiloso Restrito',false,@user1);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver3,@tenant1,@doc3,1,'restrito.pdf','.pdf',1500,'restrito.pdf',repeat('3',64),'application/pdf',@user1);
""", new { tenant1, tenant2, user1, role1, patient1, encounter1, doc1, ver1, link1, doc2, ver2, link2, doc3, ver3 });

            var factory = new LocalDbFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var service = new DemaisSetoresService(factory, null!, null!, null!, null!, null!, auth, new TestAuditWriter(), Options.Create(new DocumentUploadOptions()), new UnconfiguredClinicalSource(), NullLogger<DemaisSetoresService>.Instance);
            await service.EnsureTenantAsync(tenant1, CancellationToken.None);
            await service.EnsureTenantAsync(tenant2, CancellationToken.None);

            // 1. Prepara entrega com Doc 1 e Doc 2
            var prepReq = new SectorDeliveryRequest(
                tenant1, user1, false, encounter1, "ANEXO_I", "Setor Auditoria",
                new[] { link1, link2 }, "127.0.0.1", "TestAgent");

            var prepResult = await service.PrepareDeliveryAsync(prepReq, CancellationToken.None);
            Assert.True(prepResult.Recorded);
            Assert.NotNull(prepResult.DeliveryId);
            var deliveryId = prepResult.DeliveryId.Value;

            // 2. Consulta inicial pelo usuário autorizado -> Sucesso completo
            var details = await service.GetDeliveryAsync(tenant1, user1, false, deliveryId, CancellationToken.None);
            Assert.NotNull(details);
            Assert.Contains(details.Items, i => i.LinkId == link1);
            Assert.Contains(details.Items, i => i.LinkId == link2);

            // 3. Consulta de outro tenant -> Nega integralmente
            var otherTenantDetails = await service.GetDeliveryAsync(tenant2, user1, false, deliveryId, CancellationToken.None);
            Assert.Null(otherTenantDetails);

            // 4. Revoga permissão sobre o Documento 2 removendo a permissão da role
            await db.ExecuteAsync("delete from ged.role_permission where tenant_id=@tenant1 and permission_code='Documents.View'", new { tenant1 });

            // Usuário sem Documents.View -> GetDeliveryAsync deve negar integralmente sem vazar metadados
            var deniedDetails = await service.GetDeliveryAsync(tenant1, user1, false, deliveryId, CancellationToken.None);
            Assert.Null(deniedDetails);

            // Restaura permissão geral
            await db.ExecuteAsync("insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant1,@role1,'Documents.View')", new { tenant1, role1 });

            // 5. Corrige vínculo do Link 1 para apontar para Doc 3 e Ver 3 após a preparação
            var corrReq = new SectorCorrectionRequest(
                tenant1, user1, false, link1, encounter1,
                "Troca pós preparação", "127.0.0.1", "TestAgent",
                ToPatientId: patient1, ToDocumentId: doc3, ToVersionId: ver3);

            var corrResult = await service.CorrectLinkAsync(corrReq, CancellationToken.None);
            Assert.Null(corrResult); // Sucesso na correção do link

            // 6. Consulta do pacote preparado anteriormente:
            // O pacote foi preparado com Ver 1 (Doc 1).
            // A autorização deve resolver Doc 1 a partir da versão persistida Ver 1, e NÃO Doc 3!
            var preservedDetails = await service.GetDeliveryAsync(tenant1, user1, false, deliveryId, CancellationToken.None);
            Assert.NotNull(preservedDetails);
            // Verifica que o item do pacote aponta para ver1
            Assert.Contains(preservedDetails.Items, i => i.VersionId == ver1);

            // 7. Confirmação de entrega revalida autorização documental canônica
            var confirmErr = await service.ConfirmDeliveryAsync(tenant1, user1, false, deliveryId, "PROT-100", "Recibo assinado", "127.0.0.1", "TestAgent", CancellationToken.None);
            Assert.Null(confirmErr);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [LocalPgFact]
    public async Task Historical_snapshot_persists_missing_required_items_and_remains_stable_after_catalog_link_and_evidence_mutations()
    {
        var name = "ig_hist_" + Guid.NewGuid().ToString("N");
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
            var role = Guid.NewGuid();
            var patient = Guid.NewGuid();
            var encounter = Guid.NewGuid();

            var doc1 = Guid.NewGuid();
            var ver1 = Guid.NewGuid();
            var link1 = Guid.NewGuid();

            await db.ExecuteAsync("""
insert into ged.tenant(id,name,code) values(@tenant,'Hospital Snapshot Histórico','fixture');
insert into ged.app_user(id,tenant_id,name,email,password_hash) values(@user,@tenant,'Usuário Auditor','user@hospital.test','hash');
insert into ged.role(id,tenant_id,code,name) values(@role,@tenant,'HOSPITAL','Hospital');
insert into ged.app_role(id,tenant_id,name,normalized_name) values(@role,@tenant,'Hospital','HOSPITAL');
insert into ged.user_role(user_id,role_id) values(@user,@role);
insert into ged.permission(code,name) values('Documents.View','View') on conflict(code) do nothing;
insert into ged.role_permission(tenant_id,role_id,permission_code) values(@tenant,@role,'Documents.View');
insert into ged.sector_access(tenant_id,user_id,sector_code,permission_code) values(@tenant,@user,'FATURAMENTO','ADMINISTRAR');

insert into ged.sector_patient(id,tenant_id,source_system,source_unit,external_code,display_name,identifier_kind,confirmed_at)
values(@patient,@tenant,'MV','SADT','P100','Paciente Snapshot','prontuario',now());

insert into ged.sector_encounter(id,tenant_id,patient_id,external_code,kind,confirmed_at)
values(@encounter,@tenant,@patient,'ATD-SNAP-01','INTERNACAO',now());

insert into ged.document(id,tenant_id,code,title,is_confidential,created_by)
values(@doc1,@tenant,'DOC-SNAP-1','Prescricao Diaria',false,@user);
insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,checksum_sha256,content_type,created_by)
values(@ver1,@tenant,@doc1,1,'presc.pdf','.pdf',2000,'presc.pdf',repeat('s',64),'application/pdf',@user);

-- Vínculo com evidência física original preenchida
insert into ged.sector_document_link(id,tenant_id,patient_id,encounter_id,document_id,version_id,catalog_code,origin,original_file_name,content_sha256,uploaded_by,transfer_status,preview_status,ocr_status,extraction_status,review_status,physical_evidence)
values(@link1,@tenant,@patient,@encounter,@doc1,@ver1,'DS01','GED_UPLOAD','presc.pdf',repeat('s',64),@user,'GRAVADO','PRONTO','CONCLUIDO','CONCLUIDO','CONFERIDO','Recibo Original Físico 123');
""", new { tenant, user, role, patient, encounter, doc1, ver1, link1 });

            var factory = new LocalDbFactory(builder.ConnectionString);
            var auth = new AbacAuthorizationService(factory);
            var service = new DemaisSetoresService(factory, null!, null!, null!, null!, null!, auth, new TestAuditWriter(), Options.Create(new DocumentUploadOptions()), new UnconfiguredClinicalSource(), NullLogger<DemaisSetoresService>.Instance);
            await service.EnsureTenantAsync(tenant, CancellationToken.None);

            // Prepara entrega com apenas 1 documento (DS01), deixando de fora outros obrigatórios do perfil ANEXO_I
            var prepReq = new SectorDeliveryRequest(
                tenant, user, false, encounter, "ANEXO_I", "Recepção Central",
                new[] { link1 }, "127.0.0.1", "TestAgent");

            var prepResult = await service.PrepareDeliveryAsync(prepReq, CancellationToken.None);
            Assert.True(prepResult.Recorded);
            Assert.Equal("PREPARACAO_INCOMPLETA", prepResult.Status);
            Assert.NotNull(prepResult.DeliveryId);
            var deliveryId = prepResult.DeliveryId.Value;

            // 1. Verifica se os documentos obrigatórios ausentes foram persistidos estruturadamente no banco
            var persistedItems = (await db.QueryAsync<(string Outcome, string CatalogCode, string CatalogName, bool IsPending, string? PendingReason)>(
                "select outcome as Outcome, catalog_code as CatalogCode, catalog_name as CatalogName, is_pending as IsPending, pending_reason as PendingReason from ged.sector_delivery_item where delivery_id=@deliveryId order by item_order",
                new { deliveryId })).ToList();

            Assert.True(persistedItems.Count > 1, "Deveria ter persistido o item selecionado e os itens obrigatórios ausentes.");
            var missingItems = persistedItems.Where(i => i.Outcome == "DOCUMENTO_OBRIGATORIO_AUSENTE").ToList();
            Assert.NotEmpty(missingItems);
            Assert.All(missingItems, m =>
            {
                Assert.True(m.IsPending);
                Assert.Equal("Obrigatório neste contexto e ausente da seleção", m.PendingReason);
            });

            // 2. Consulta a preparação e comprova fotografia histórica
            var details1 = await service.GetDeliveryAsync(tenant, user, false, deliveryId, CancellationToken.None);
            Assert.NotNull(details1);
            Assert.True(details1.IsHistoricalSnapshot);
            var selectedItem = details1.Items.First(i => i.LinkId == link1);
            Assert.Equal("Recibo Original Físico 123", selectedItem.PhysicalEvidence);

            // 3. Modifica dados correntes (catálogo e evidência do vínculo) para simular alterações futuras
            await db.ExecuteAsync("""
update ged.sector_document_type
set name='Evolução Médica Nome Alterado Futuro', physical_original=false
where tenant_id=@tenant and catalog_code='EVOLUCAO';

update ged.sector_document_link
set physical_evidence='Evidência Alterada 999 Futura'
where tenant_id=@tenant and id=@link1;
""", new { tenant, link1 });

            // 4. Reconsulta o pacote (reimpressão):
            // DEVE manter o snapshot histórico gravado no momento da preparação!
            var detailsAfterMutation = await service.GetDeliveryAsync(tenant, user, false, deliveryId, CancellationToken.None);
            Assert.NotNull(detailsAfterMutation);
            Assert.True(detailsAfterMutation.IsHistoricalSnapshot);

            var recheckItem = detailsAfterMutation.Items.First(i => i.LinkId == link1);
            // Evidência física NÃO deve ter sido alterada para a nova
            Assert.Equal("Recibo Original Físico 123", recheckItem.PhysicalEvidence);
            // Nome gravado no snapshot não foi contaminado
            Assert.DoesNotContain("Nome Alterado Futuro", recheckItem.Name);

            // Os itens obrigatórios ausentes permanecem estáveis no snapshot
            Assert.Contains(detailsAfterMutation.Items, i => i.Outcome == "DOCUMENTO_OBRIGATORIO_AUSENTE");

            // 5. Validação de registro legado:
            // Insere um registro sem as colunas de snapshot histórico
            var legacyDeliveryId = Guid.NewGuid();
            var legacyLinkId = Guid.NewGuid();
            var legacyProfileId = await db.ExecuteScalarAsync<Guid>("select id from ged.sector_profile where tenant_id=@tenant and code='ANEXO_I' limit 1", new { tenant });

            await db.ExecuteAsync("""
insert into ged.sector_delivery(id,tenant_id,encounter_id,profile_id,version_no,status,assembled_by,created_at)
values(@legacyDeliveryId,@tenant,@encounter,@legacyProfileId,1,'PREPARADO',@user,now() - interval '30 days');

insert into ged.sector_delivery_item(delivery_id,link_id,version_id,outcome,detail,item_order,catalog_code,is_pending)
values(@legacyDeliveryId,@legacyLinkId,@ver1,'PREPARADO','Registro legado',1,'EVOLUCAO',false);
""", new { legacyDeliveryId, tenant, encounter, legacyProfileId, user, legacyLinkId, ver1 });

            var legacyDetails = await service.GetDeliveryAsync(tenant, user, false, legacyDeliveryId, CancellationToken.None);
            Assert.NotNull(legacyDetails);
            Assert.False(legacyDetails.IsHistoricalSnapshot);
            var legacyItem = legacyDetails.Items.First(i => i.LinkId == legacyLinkId);
            // Não deve inventar dados atuais como históricos comprovados
            Assert.Null(legacyItem.PhysicalEvidence);
        }
        finally
        {
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
        start.Environment["ConnectionStrings__DefaultConnection"] = dsn;
        var proc = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Falha ao iniciar Migrator");
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            var outText = await proc.StandardOutput.ReadToEndAsync();
            throw new InvalidOperationException($"Migrator install falhou com código {proc.ExitCode}: {err}\n{outText}");
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InovaGed.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("InovaGed.sln não encontrado");
    }

    private sealed class SegmentedTestAiGateway : IDocumentAiGateway, IAiGovernanceStore
    {
        public int TotalExecutions;
        public int? FailOnSegmentIndex;
        private int currentExecutionIndex = 0;

        public IReadOnlyDictionary<string, AiCapabilities> Capabilities => new Dictionary<string, AiCapabilities>();
        public int MaxOutputTokens => 1000;

        public async Task<AiResult> ExecuteAsync(AiRequest request, CancellationToken ct)
        {
            TotalExecutions++;
            var executionIndex = currentExecutionIndex++;

            var execId = Guid.NewGuid();
            if (request.OnExecutionReserved is not null)
                await request.OnExecutionReserved(execId, ct);

            var promptText = request.Context.FirstOrDefault()?.Text ?? "";
            var items = new List<Dictionary<string, object?>>();

            if (promptText.Contains("Omeprazol"))
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["category"] = "medicamento",
                    ["name"] = "Omeprazol",
                    ["dose"] = "40 mg",
                    ["unit"] = "mg",
                    ["route"] = "oral",
                    ["frequency"] = "em jejum",
                    ["quantity"] = "",
                    ["date"] = "",
                    ["context"] = "PRESCRITO",
                    ["evidence"] = "Prescrito Omeprazol 40 mg via oral em jejum."
                });
            }
            else if (promptText.Contains("Amoxicilina"))
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["category"] = "medicamento",
                    ["name"] = "Amoxicilina",
                    ["dose"] = "500 mg",
                    ["unit"] = "mg",
                    ["route"] = "oral",
                    ["frequency"] = "a cada 8 horas",
                    ["quantity"] = "",
                    ["date"] = "",
                    ["context"] = "PRESCRITO",
                    ["evidence"] = "Prescrito Amoxicilina 500 mg via oral a cada 8 horas."
                });
            }
            else if (promptText.Contains("Dipirona"))
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["category"] = "medicamento",
                    ["name"] = "Dipirona",
                    ["dose"] = "1000 mg",
                    ["unit"] = "mg",
                    ["route"] = "intravenosa",
                    ["frequency"] = "em caso de dor",
                    ["quantity"] = "",
                    ["date"] = "",
                    ["context"] = "PRESCRITO",
                    ["evidence"] = "Prescrito Dipirona 1000 mg via intravenosa em caso de dor."
                });
            }

            var json = JsonSerializer.Serialize(new { items });
            var output = JsonDocument.Parse(json);

            var usage = new AiUsage(10, 10, 20);
            if (FailOnSegmentIndex.HasValue && executionIndex == FailOnSegmentIndex.Value)
            {
                return new AiResult(
                    Success: false,
                    Text: null,
                    StructuredData: null,
                    Usage: usage,
                    Provider: "Synthetic",
                    Model: "fixture-v1",
                    Failure: AiFailureKind.Timeout,
                    Limitation: "Falha simulada na chamada de IA para o segmento.",
                    CorrelationId: null,
                    ProviderReached: false,
                    ExecutionId: execId);
            }

            var result = new AiResult(
                Success: true,
                Text: null,
                StructuredData: output,
                Usage: usage,
                Provider: "Synthetic",
                Model: "fixture-v1",
                Failure: AiFailureKind.None,
                Limitation: null,
                CorrelationId: null,
                ProviderReached: true,
                ExecutionId: execId);

            executions[execId] = new AiExecutionStatus(
                execId,
                request.TenantId,
                request.UserId,
                request.Task,
                AiExecutionState.Completed,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                result,
                request.SourceDocuments,
                DateTimeOffset.UtcNow.AddHours(1),
                AiSourceIntegrity.Resolved);

            return result;
        }

        private readonly Dictionary<Guid, AiExecutionStatus> executions = new();

        public Task<AiExecutionStatus?> GetExecutionAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct)
            => Task.FromResult(executions.TryGetValue(id, out var val) ? (AiExecutionStatus?)val : null);
        public Task<AiEffectivePolicy?> GetEffectivePolicyAsync(Guid t, AiTask task, CancellationToken ct) => throw new NotSupportedException();
        public Task<AiExecutionLease> ReserveAsync(AiRequest r, string p, string m, long revision, long estimate, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> MarkRunningAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> MarkSentAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task CompleteAsync(Guid id, AiResult r, long tokens, TimeSpan duration, CancellationToken ct) => Task.CompletedTask;
        public Task MigrateVerifiedLegacySourcesAsync(Guid t, Guid u, Guid id, IReadOnlyList<AiExecutionSource> s, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> ExpireReservationsAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<AiRecoveryHealth> GetRecoveryHealthAsync(CancellationToken ct) => throw new NotSupportedException();
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

    private sealed class TestAuditWriter : InovaGed.Application.Audit.IAuditWriter
    {
        public Task<InovaGed.Domain.Primitives.Result> WriteAsync(InovaGed.Application.Audit.AuditWriteCommand command, CancellationToken ct) => Task.FromResult(InovaGed.Domain.Primitives.Result.Ok());
        public Task<InovaGed.Domain.Primitives.Result> WriteAsync(Guid tenantId, Guid? userId, string action, string entityName, Guid? entityId, string? summary, string? ipAddress, string? userAgent, object? data, CancellationToken ct)
            => Task.FromResult(InovaGed.Domain.Primitives.Result.Ok());
    }
}

