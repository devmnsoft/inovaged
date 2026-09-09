using Dapper;
using Npgsql;

namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsCanvasProductionRc25QualityCheck
{
    private static readonly string[] ClientTemplates=["CLIENTE_CAIXA_CANVAS_V1","CLIENTE_DOCUMENTO_CANVAS_V1","CLIENTE_PASTA_CANVAS_V1","CLIENTE_PRONTUARIO_CANVAS_V1","CLIENTE_PROCESSO_CANVAS_V1"];
    public static async Task<int> RunAsync(string root,string? connectionString,TextWriter output,TextWriter error,CancellationToken ct)
    {
        var failures=new List<string>();
        void Check(bool ok,string name,string detail){if(ok)output.WriteLine($"[OK] {name}");else{failures.Add(name);error.WriteLine($"[FALHA] {name}: {detail}");}}
        string Read(params string[] parts){var path=Path.Combine([root,..parts]);return File.Exists(path)?File.ReadAllText(path):"";}
        var contracts=Read("InovaGed.Application","Labels","Canvas","LabelCanvasContracts.cs");var repository=Read("InovaGed.Infrastructure","Labels","LabelCanvasDesignRepository.cs");var renderer=Read("InovaGed.Infrastructure","Labels","LabelCanvasRenderService.cs");var coordinator=Read("InovaGed.Infrastructure","Labels","LabelCanvasPrintCoordinator.cs");var catalog=Read("InovaGed.Infrastructure","PhysicalArchive","LabelTemplateCatalogService.cs");var jobs=Read("InovaGed.Infrastructure","PhysicalArchive","LabelPrintJobService.cs");var labels=Read("InovaGed.Web","Controller","LabelsController.cs");var designer=Read("InovaGed.Web","Controller","LabelDesignerController.cs");var edit=Read("InovaGed.Web","Views","Labels","Designer","Edit.cshtml");var js=Read("InovaGed.Web","wwwroot","js","labels-designer.js");var branding=Read("InovaGed.Web","Controller","PrintBrandingController.cs");
        Check(contracts.Contains("LabelCanvasSubjectTypeMapper")&&new[]{"LOCDESKBOX","MEDICALRECORD","LOCDESKFOLDER","BATCH"}.All(contracts.Contains),"Mapeamento semântico único","O mapper não cobre os tipos obrigatórios.");
        Check(catalog.Contains("LabelCanvasSubjectTypeMapper.ToOperational")&&!catalog.Contains("case when upper(subject_type)"),"Catálogo sem CASE duplicado","O catálogo não utiliza o mapper único.");
        Check(contracts.Contains("ILabelCanvasPrintCoordinator")&&coordinator.Contains("GetPublishedAsync")&&coordinator.Contains("valueResolver.ResolveAsync")&&coordinator.Contains("ResolveBrandingAsync")&&coordinator.Contains("ResolveCalibrationAsync"),"Pipeline Canvas única","Coordenador incompleto.");
        Check(repository.Contains("GetPublishedAsync")&&repository.Contains("BeginRevisionAsync")&&repository.Contains("join lateral")&&repository.Contains("BuildVersionSnapshot"),"Versão publicada e revisão","Snapshot/envelope ou separação draft/publicado ausente.");
        Check(catalog.Contains("label_template_design_version")&&!catalog.Contains("status='PUBLISHED' and reg_status"),"Draft não remove produção","Catálogo ainda depende apenas do status da working copy.");
        Check(renderer.Contains("BuildCode128Svg")&&renderer.Contains("codes.Add(106)")&&renderer.Contains("<rect x=")&&!renderer.Contains("repeating-linear-gradient"),"Barcode Code 128 real","Barcode ainda é decorativo.");
        Check(contracts.Contains("GroupId")&&contracts.Contains("SchemaVersion { get; set; } = 2")&&js.Contains("documentModel.schemaVersion=2"),"Schema v2 e agrupamento","groupId/schemaVersion 2 não persistem.");
        Check(new[]{"UPPERCASE","DATE_DDMMYYYY","PAD_LEFT","HAS_VALUE","NOT_EQUALS"}.All(renderer.Contains),"Formatos e visibilidade segura","Formatadores ou condições obrigatórias ausentes.");
        Check(renderer.Contains("REQUIRED_VALUE")&&renderer.Contains("QR_RUNTIME_PAYLOAD")&&renderer.Contains("REQUIRED_LOGO"),"Validação runtime","Valores resolvidos obrigatórios não são validados.");
        Check(renderer.Contains("^/Administration/BrandAssets/")&&renderer.Contains("Convert.FromBase64String")&&!renderer.Contains("source.StartsWith(\"/\""),"Origem de imagem segura","Imagem aceita URL/caminho arbitrário.");
        Check(designer.Contains("IPrintBrandingResolver")&&designer.Contains("/Labels/Designer/LivePreview"),"Preview com branding","Preview real não usa branding.");
        Check(labels.Contains("_canvasPrintCoordinator.PrepareAsync")&&labels.Contains("CanvasLabel")&&jobs.Contains("PrepareBatchAsync")&&!jobs.Contains("canvasRenderer.RenderBatch"),"Wizard/job/batch Canvas","Algum caminho ignora o coordenador.");
        Check(labels.Contains("Encoding.UTF8.GetString(rendered.Content)")&&labels.Contains("template?.ViewName,\"CanvasLabel\""),"PrintPreview Canvas sem JSON","Preview de job não encaminha o HTML Canvas.");
        Check(edit.Contains("data-layers")&&edit.Contains("data-marquee")&&edit.Contains("data-snap")&&edit.Contains("smart-guides")&&js.Contains("rotation-handle")&&js.Contains("dragstart")&&js.Contains("guidesFor"),"Editor visual profissional","Layers, handles, guides, marquee ou drag estão ausentes.");
        Check(branding.Contains("label_template_design")&&branding.Contains("LABEL_TEMPLATE"),"Bindings dinâmicos","Templates do tenant não alimentam os bindings.");
        if(string.IsNullOrWhiteSpace(connectionString))output.WriteLine("[AVISO] Banco não configurado; checks PostgreSQL omitidos.");
        else try
        {
            await using var db=new NpgsqlConnection(connectionString);await db.OpenAsync(ct);
            var count=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(distinct template_key) from ged.label_template_design where template_key=any(@keys) and reg_status in ('A','ACTIVE')",new{keys=ClientTemplates},cancellationToken:ct));Check(count==5,"Cinco templates CLIENTE_*","Foram encontrados menos de cinco templates.");
            var published=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(distinct d.template_key) from ged.label_template_design d join ged.label_template_design_version v on v.template_design_id=d.id and v.status='PUBLISHED' and v.reg_status in ('A','ACTIVE') where d.template_key=any(@keys) and d.reg_status in ('A','ACTIVE')",new{keys=ClientTemplates},cancellationToken:ct));Check(published==5,"Cinco versões publicadas","Algum template CLIENTE_* não possui snapshot publicado.");
        }
        catch(Exception ex){failures.Add("PostgreSQL");error.WriteLine($"[FALHA] PostgreSQL: {ex.GetType().Name}: {ex.Message}");}
        output.WriteLine($"RC25 Label Canvas Production Studio: {failures.Count} falha(s).");return failures.Count==0?0:2;
    }
}
