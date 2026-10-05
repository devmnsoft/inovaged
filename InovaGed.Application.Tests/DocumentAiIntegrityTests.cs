using System.Text.Json;
using InovaGed.Application.ArtificialIntelligence;

namespace InovaGed.Application.Tests;

public sealed class DocumentAiIntegrityTests
{
    [Fact]
    public void Confidentiality_keeps_the_current_value_when_the_field_is_not_selected()
    {
        var decision = ConfidentialityDecision.Resolve(current: true, selected: false, proposed: false, canChangeSecrecy: true);
        Assert.False(decision.Changed);
        Assert.Equal(true, decision.Applied);
        Assert.Null(decision.Error);
    }

    [Fact]
    public void Confidentiality_applies_the_reviewer_value_not_the_selection_flag()
    {
        var raise = ConfidentialityDecision.Resolve(false, true, true, true);
        var lower = ConfidentialityDecision.Resolve(true, true, false, true);
        Assert.True(raise.Changed); Assert.Equal(true, raise.Applied);
        Assert.True(lower.Changed); Assert.Equal(false, lower.Applied);
    }

    [Fact]
    public void Confidentiality_requires_a_value_and_the_specific_permission()
    {
        var missing = ConfidentialityDecision.Resolve(false, true, null, true);
        Assert.Null(missing.Applied);
        Assert.Contains("valor", missing.Error, StringComparison.OrdinalIgnoreCase);

        var denied = ConfidentialityDecision.Resolve(true, true, false, false);
        Assert.Null(denied.Applied);
        Assert.Contains("permissão", denied.Error, StringComparison.OrdinalIgnoreCase);

        var same = ConfidentialityDecision.Resolve(true, true, true, false);
        Assert.False(same.Changed);
        Assert.Equal(true, same.Applied);
        Assert.Null(same.Error);
    }

    [Fact]
    public void Metadata_reader_drops_one_field_without_rejecting_the_others()
    {
        using var json = JsonDocument.Parse("""
        {"title":{"sufficient":false},"description":{"sufficient":true,"value":"Laudo","evidence":"Laudo de exame"},"isConfidential":{"sufficient":true,"value":false,"evidence":"Laudo de exame"}}
        """);
        var fields = MetadataSuggestionReader.Read(json.RootElement, "Laudo de exame sem dado sensível");
        Assert.False(fields.Single(x => x.Name == "title").Sufficient);
        Assert.Equal("Laudo", fields.Single(x => x.Name == "description").Text);
        var secrecy = fields.Single(x => x.Name == "isConfidential");
        Assert.False(secrecy.Sufficient);
        Assert.Contains("público", secrecy.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Metadata_reader_accepts_secrecy_only_when_the_text_supports_true()
    {
        using var json = JsonDocument.Parse("""
        {"title":{"sufficient":true,"value":"Sem evidência","evidence":"não está no texto"},"description":{"note":"ausente"},"isConfidential":{"sufficient":true,"value":true,"evidence":"sigiloso"}}
        """);
        var fields = MetadataSuggestionReader.Read(json.RootElement, "Documento sigiloso do paciente");
        Assert.False(fields.Single(x => x.Name == "title").Sufficient);
        Assert.False(fields.Single(x => x.Name == "description").Sufficient);
        Assert.Equal(true, fields.Single(x => x.Name == "isConfidential").Flag);
    }

    [Fact]
    public void Catalog_reader_ignores_self_declared_confidence_and_allows_no_suggestion()
    {
        using var insufficient = JsonDocument.Parse("""{"outcome":"insufficient","typeName":"Contrato","confidence":0.99,"evidence":"contrato"}""");
        var none = CatalogSuggestionReader.Read(insufficient.RootElement, "contrato social", "typeName");
        Assert.False(none.HasSuggestion);
        Assert.Contains("calibrada", none.Note, StringComparison.OrdinalIgnoreCase);

        using var suggested = JsonDocument.Parse("""{"outcome":"suggested","classCode":"01.02","modelIndicator":80,"evidence":"prontuário","justification":"código do plano"}""");
        var match = CatalogSuggestionReader.Read(suggested.RootElement, "prontuário hospitalar", "classCode");
        Assert.True(match.HasSuggestion);
        Assert.Equal("01.02", match.Name);
        Assert.Contains("calibrada", match.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_codec_distinguishes_legacy_format_from_corruption()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var legacy = $"[\"{documentId:N}/{versionId:N}/texto-extraido\"]";
        var resolved = AiExecutionSourceCodec.Read($"[{{\"documentId\":\"{documentId:N}\",\"versionId\":\"{versionId:N}\"}}]", "not-json");
        Assert.Equal(AiSourceIntegrity.Resolved, resolved.Integrity);
        Assert.Equal(documentId, resolved.Sources[0].DocumentId);

        var legacyRead = AiExecutionSourceCodec.Read("[]", legacy);
        Assert.Equal(AiSourceIntegrity.LegacyFormat, legacyRead.Integrity);
        Assert.Equal(versionId, legacyRead.Sources[0].VersionId);

        Assert.Equal(AiSourceIntegrity.Corrupted, AiExecutionSourceCodec.Read("{", legacy).Integrity);
        Assert.Equal(AiSourceIntegrity.Corrupted, AiExecutionSourceCodec.Read("[]", "[\"documento-arbitrario\"]").Integrity);
        Assert.Equal(AiSourceIntegrity.Corrupted, AiExecutionSourceCodec.Read("[]", "not-json").Integrity);
        Assert.Equal(AiSourceIntegrity.Missing, AiExecutionSourceCodec.Read("[]", "[]").Integrity);
        Assert.Equal(AiSourceIntegrity.Missing, AiExecutionSourceCodec.Read(null, null).Integrity);
    }

    [Fact]
    public void AskCollection_is_configurable_and_unfinished_tasks_stay_explained()
    {
        Assert.Contains(AiTask.AskCollection, AiTaskCatalog.Supported);
        Assert.Contains(AiTask.SuggestClassification, AiTaskCatalog.Supported);
        Assert.Contains(AiTask.SuggestArchivalClassification, AiTaskCatalog.Supported);
        Assert.Equal("Sugerir tipo documental", AiTaskCatalog.Label(AiTask.SuggestClassification));
        Assert.Equal("Sugerir classificação arquivística", AiTaskCatalog.Label(AiTask.SuggestArchivalClassification));
        Assert.Equal("Pergunte ao acervo", AiTaskCatalog.Label(AiTask.AskCollection));
        Assert.DoesNotContain(AiTask.SupportProtocol, AiTaskCatalog.Supported);
        Assert.DoesNotContain(AiTask.CompareDocuments, AiTaskCatalog.Supported);
        Assert.False(string.IsNullOrWhiteSpace(AiTaskCatalog.UnavailableReason(AiTask.SupportProtocol)));
        Assert.False(string.IsNullOrWhiteSpace(AiTaskCatalog.UnavailableReason(AiTask.CompareDocuments)));
    }
}
