using InovaGed.Application.Sectors;
using InovaGed.Application.Tests.Ged;
using InovaGed.Infrastructure.Sectors;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Dapper;

namespace InovaGed.Application.Tests.Sectors;

public sealed class DemaisSetoresRulesTests
{
    [Fact]
    public void Catalog_keeps_the_thirty_three_names_in_order()
    {
        Assert.Equal(33, SectorCatalog.All.Count);
        Assert.Equal("DS01", SectorCatalog.All[0].Code);
        Assert.Equal("DS33", SectorCatalog.All[^1].Code);
        Assert.Equal("Ficha de Atendimento da Internação.", SectorCatalog.All[0].Name);
        Assert.Equal("Sistematização da Assistência de Enfermagem — POI.", SectorCatalog.Find("DS30")!.Name);
        Assert.DoesNotContain(SectorCatalog.All, item => item.ViewSectors.Contains("DDI"));
    }

    [Fact]
    public void Anexo_I_is_not_required_for_ambulatory_or_unmapped_kind()
    {
        var conditions = new SectorConditions(true, true, true, true);
        Assert.False(SectorApplicability.IsRequired("ANEXO_I", true, "AMBULATORIAL", SectorCatalog.HospitalInternacao, conditions));
        Assert.False(SectorApplicability.IsRequired("ANEXO_I", true, "URGENCIA", SectorCatalog.Surgery, conditions));
        Assert.False(SectorApplicability.IsRequired("ANEXO_I", true, null, SectorCatalog.HospitalInternacao, conditions));
        Assert.True(SectorApplicability.IsRequired("ANEXO_I", true, "INTERNACAO", SectorCatalog.HospitalInternacao, conditions));
        Assert.False(SectorApplicability.IsRequired("ANEXO_I", true, "INTERNACAO", SectorCatalog.Surgery, new SectorConditions(false, false, false, false)));
        Assert.False(SectorApplicability.IsRequired("ANEXO_I", true, "INTERNACAO", SectorCatalog.WhenApplicable, conditions));
        Assert.False(SectorApplicability.IsRequired("ANEXO_II", false, "INTERNACAO", SectorCatalog.HospitalInternacao, conditions));
    }

    [Fact]
    public void External_codes_keep_leading_zeros_and_unmapped_kinds()
    {
        Assert.Equal("000120", SectorPatientCode.Normalize("000120"));
        Assert.Null(SectorKindMap.Resolve("I", new Dictionary<string, string>()));
        Assert.Null(SectorKindMap.Resolve("A", new Dictionary<string, string>()));
        Assert.Null(SectorKindMap.Resolve("U", new Dictionary<string, string>()));
        Assert.Equal("INTERNACAO", SectorKindMap.Resolve("I", new Dictionary<string, string> { ["I"] = "INTERNACAO" }));
    }

    [Fact]
    public void Not_applicable_requires_justification_and_pending_cannot_become_replaced()
    {
        Assert.False(SectorReview.CanTransition(SectorReview.Pendente, SectorReview.NaoAplicavel, SectorPermissions.Conferir, "  ", out var missing));
        Assert.Contains("justificativa", missing, StringComparison.OrdinalIgnoreCase);
        Assert.True(SectorReview.CanTransition(SectorReview.Pendente, SectorReview.NaoAplicavel, SectorPermissions.Conferir, "não se aplica a este caso", out _));
        Assert.False(SectorReview.CanTransition(SectorReview.Pendente, SectorReview.Substituido, SectorPermissions.Administrar, "troca", out _));
    }

    [Fact]
    public void Extraction_does_not_treat_prescription_as_performed_procedure()
    {
        var text = """
        --- página 2 ---
        Dipirona 500 mg via oral
        Procedimento adicional planejado
        """;
        var prescription = SectorExtraction.Read(text, "DS23");
        Assert.Contains(prescription, item => item.Context == SectorExtraction.Prescrito && item.Page == 2);
        Assert.Contains(prescription, item => item.Context == SectorExtraction.Planejado && item.Category == "procedimento");
        Assert.DoesNotContain(prescription, item => item.Context == SectorExtraction.MencionadoComoRealizado);

        var surgery = SectorExtraction.Read("--- página 4 ---\nProcedimento realizado: drenagem", "DS08");
        Assert.Contains(surgery, item => item.Context == SectorExtraction.MencionadoComoRealizado && item.Page == 4);
        Assert.Null(SectorTextLocator.PageAt("sem marcação de página", 0));
    }

    [Fact]
    public void Delivery_blocks_failed_files_and_physical_originals_without_evidence()
    {
        var blockers = SectorDeliveryRules.Blockers(
        [
            new("Nota Fiscal da OPME.", true, false, false),
            new("Descrição Cirúrgica.", false, false, true)
        ]);
        Assert.Equal(2, blockers.Count);
        Assert.Contains(blockers, item => item.Contains("original físico", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blockers, item => item.Contains("falha individual", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_notice_hides_a_restricted_document()
    {
        var hidden = SectorDuplicateNotice.Message(false, "Prescrição Médica.");
        Assert.DoesNotContain("Prescrição", hidden, StringComparison.Ordinal);
        Assert.Contains("restrito", hidden, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(ClinicalSourceMessages.Unavailable, ClinicalSourceMessages.NotFound);
        Assert.NotEqual(ClinicalSourceMessages.Failed, ClinicalSourceMessages.EncounterMissing);
    }

    [Fact]
    public void Search_uses_a_parameterized_synonym_without_inventing_a_page()
    {
        var patterns = SectorSearchTerms.Patterns("apac", [("Autorização de Procedimentos de Alto Custo — APAC.", ["APAC"])]);
        Assert.Contains("%apac%", patterns);
        Assert.Contains(patterns, item => item.Contains("Alto Custo", StringComparison.Ordinal));
        Assert.Equal(-1, SectorSearchTerms.FirstIndex("outro texto", patterns));
    }

    [Fact]
    public void Migration_seed_contains_every_catalog_name_and_does_not_update_existing_rows()
    {
        var sql = File.ReadAllText(Path.Combine(FindRoot(), "database", "migrations", "2026_10_25_demais_setores.sql"));
        foreach (var item in SectorCatalog.All)
            Assert.Contains(item.Name, sql, StringComparison.Ordinal);
        Assert.Contains("where not exists", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update ged.sector_document_type", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mapeamento pendente", sql, StringComparison.Ordinal);
        var access = File.ReadAllText(Path.Combine(FindRoot(), "database", "migrations", "2026_10_26_demais_setores_acesso.sql"));
        Assert.Contains("ux_sector_patient_identity", access, StringComparison.Ordinal);
        Assert.DoesNotContain("update ged.sector_document_type", access, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Embed_link_carries_the_version_and_opening_urls_hide_the_identifier()
    {
        var version = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var link = SectorEmbedLink.Build(new string('a', 64), version);
        Assert.Contains("versionId=11111111-1111-1111-1111-111111111111", link, StringComparison.Ordinal);
        Assert.True(SectorEmbedLink.OriginMatches("https://parceiro.exemplo", " "));
        Assert.False(SectorEmbedLink.OriginMatches("https://parceiro.exemplo", "https://outro.exemplo/caminho"));
        Assert.Equal("52998224725", SectorCpf.Normalize("529.982.247-25"));
        Assert.Null(SectorCpf.Normalize("000120"));
        var opening = SectorOpeningLink.Path(new string('b', 64));
        Assert.StartsWith("/DemaisSetores/Abrir/", opening, StringComparison.Ordinal);
        Assert.False(SectorOpeningLink.Exposes(opening, "52998224725"));
        Assert.False(SectorOpeningLink.Exposes(link, "52998224725"));
    }

    [Fact]
    public void Empty_selection_does_not_count_as_a_complete_package()
    {
        var blockers = SectorDeliveryRules.Blockers([]);
        Assert.Contains(blockers, item => item.Contains("Nenhum documento", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Disabled_clinical_source_reports_unavailable_without_a_patient()
    {
        var source = new ConfigurableClinicalSource(new HttpClient(), Options.Create(new ClinicalSourceOptions()), NullLogger<ConfigurableClinicalSource>.Instance);
        var result = await source.FindAsync(Guid.NewGuid(), "000120", "MV", "1", "ID", CancellationToken.None);
        Assert.Equal(ClinicalSourceStatus.Unavailable, result.Status);
        Assert.Null(result.Patient);
        Assert.Contains("Nenhum paciente foi simulado", result.Message, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}

public sealed class DemaisSetoresCatalogPostgresTests
{
    [LocalPgFact]
    public async Task Seed_is_idempotent_and_keeps_a_customized_name()
    {
        var name = "ig_setores_" + Guid.NewGuid().ToString("N");
        var admin = new NpgsqlConnectionStringBuilder(LocalPg.AdminDsn) { Pooling = false };
        await using var control = new NpgsqlConnection(admin.ConnectionString);
        await control.OpenAsync();
        await control.ExecuteAsync($"create database {name}");
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = name };
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync("create schema if not exists ged; create table ged.tenant (id uuid primary key);");
            var tenant = Guid.NewGuid();
            await connection.ExecuteAsync("insert into ged.tenant (id) values (@tenant)", new { tenant });
            var sql = await File.ReadAllTextAsync(Path.Combine(FindRoot(), "database", "migrations", "2026_10_25_demais_setores.sql"));
            await connection.ExecuteAsync(sql);
            var access = await File.ReadAllTextAsync(Path.Combine(FindRoot(), "database", "migrations", "2026_10_26_demais_setores_acesso.sql"));
            await connection.ExecuteAsync(access);
            await connection.ExecuteAsync(sql);
            await connection.ExecuteAsync(access);
            var count = await connection.ExecuteScalarAsync<int>("select count(*) from ged.sector_document_type where tenant_id=@tenant", new { tenant });
            Assert.Equal(33, count);
            var pending = await connection.ExecuteScalarAsync<int>("select count(*) from ged.sector_type_mapping where tenant_id=@tenant and mapping_status='PENDENTE'", new { tenant });
            Assert.Equal(33, pending);
            var anexoIi = await connection.ExecuteScalarAsync<bool>("select enabled from ged.sector_profile where tenant_id=@tenant and code='ANEXO_II' and version_no=1", new { tenant });
            Assert.False(anexoIi);
            var ddi = await connection.ExecuteScalarAsync<int>("select count(*) from ged.sector_document_type where tenant_id=@tenant and 'DDI' = any(view_sectors)", new { tenant });
            Assert.Equal(0, ddi);
            await connection.ExecuteAsync("update ged.sector_document_type set name='Nome institucional preservado', customized=true where tenant_id=@tenant and catalog_code='DS19'", new { tenant });
            await connection.ExecuteAsync(sql);
            var preserved = await connection.ExecuteScalarAsync<string>("select name from ged.sector_document_type where tenant_id=@tenant and catalog_code='DS19'", new { tenant });
            Assert.Equal("Nome institucional preservado", preserved);
            var firstPhysical = await connection.ExecuteScalarAsync<string>("""
select catalog_code from ged.sector_profile_item i
join ged.sector_profile p on p.id=i.profile_id
where p.tenant_id=@tenant and p.code='ANEXO_II'
order by i.position limit 1
""", new { tenant });
            Assert.Equal("DS01", firstPhysical);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await control.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InovaGed.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException();
    }
}
