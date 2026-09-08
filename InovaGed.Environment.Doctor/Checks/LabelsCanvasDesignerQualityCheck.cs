using System.Xml.Linq;
using Dapper;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Infrastructure.Labels;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsCanvasDesignerQualityCheck
{
    private static readonly string[] Routes=["/Labels/Designer","/Labels/Designer/New","/Labels/Designer/Edit/{templateKey}","/Labels/Designer/Preview/{templateKey}","/Labels/Designer/Publish/{templateKey}","/Labels/Designer/Versions/{templateKey}","/Labels/Designer/Duplicate/{templateKey}","/Labels/Designer/DeleteDraft/{templateKey}","/Labels/Designer/TestPrint/{templateKey}","/Labels/Designer/Fields"];
    private static readonly string[] Seeds=["LOCDESK_PASTA_CANVAS_V1","LOCDESK_CAIXA_CANVAS_V1","HOL_PRONTUARIO_CANVAS_V1","GED_DOCUMENTO_CANVAS_V1","GED_CAIXA_CANVAS_V1"];

    public static async Task<int> RunAsync(string root,string? connectionString,TextWriter output,TextWriter error,CancellationToken ct)
    {
        var failures=new List<string>();var warnings=new List<string>();
        void Check(bool condition,string name,string message){if(condition)output.WriteLine($"[OK] {name}");else{failures.Add(name);error.WriteLine($"[FALHA] {name}: {message}");}}
        var controller=Read(root,"InovaGed.Web","Controller","LabelDesignerController.cs");
        var migration=Read(root,"database","migrations","2026_09_08_label_canvas_designer_rc22.sql");
        Check(Routes.All(controller.Contains),"Rotas do designer","Uma ou mais rotas obrigatórias não foram encontradas.");
        Check(File.Exists(Path.Combine(root,"InovaGed.Web","wwwroot","css","labels-designer.css"))&&File.Exists(Path.Combine(root,"InovaGed.Web","wwwroot","js","labels-designer.js")),"Assets CSS/JS","Os assets do designer não existem.");
        Check(new[]{"label_template_design","label_template_design_version","label_template_design_event","using gin","template_key","design_json"}.All(migration.Contains),"Migration RC22","A migration não contém toda a estrutura obrigatória.");
        Check(Seeds.All(migration.Contains),"Seeds canvas","Um ou mais templates iniciais estão ausentes.");
        Check(new[]{"drag","resize","undo","redo","toggle-grid","toggle-ruler","group","ungroup","finally"}.All(Read(root,"InovaGed.Web","wwwroot","js","labels-designer.js").Contains),"Interações do canvas","A implementação JS não cobre todas as interações mínimas.");
        try
        {
            var renderer=new LabelCanvasRenderService(NullLogger<LabelCanvasRenderService>.Instance);
            var json="""{"schemaVersion":1,"canvas":{"widthMm":100,"heightMm":70,"paper":"A4","orientation":"portrait","gridMm":2,"safeMarginMm":3},"elements":[{"id":"logo","type":"logo","name":"Logo","xMm":5,"yMm":5,"widthMm":20,"heightMm":10,"visible":true,"style":{},"binding":{"asset":"data:,","fallback":"Logo"},"validation":{}}],"bindings":{"subjectType":"Document","sampleDataProfile":"Documento GED"}}""";
            var html=renderer.Render(new LabelCanvasDesignDto{TemplateKey="DOCTOR",TemplateName="Doctor",DesignJson=json},new Dictionary<string,object?>()).Html;
            Check(!html.Contains("src=\"\"")&&!html.Contains("src=\"data:,\"")&&!html.Contains("src=\"/data:,\""),"Renderização segura de imagem","O HTML contém src inválido.");
        }
        catch(Exception exception){failures.Add("Renderização");error.WriteLine($"[FALHA] Renderização: {exception.Message}");}
        var webConfig=Directory.EnumerateFiles(root,"web.config",SearchOption.AllDirectories).FirstOrDefault();
        try{if(webConfig is not null)XDocument.Load(webConfig);output.WriteLine("[OK] web.config válido ou não aplicável");}catch(Exception exception){failures.Add("web.config");error.WriteLine($"[FALHA] web.config: {exception.Message}");}
        if(string.IsNullOrWhiteSpace(connectionString))warnings.Add("Banco não configurado; checks PostgreSQL não foram executados.");
        else
        {
            try
            {
                await using var db=new NpgsqlConnection(connectionString);await db.OpenAsync(ct);
                var tables=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from information_schema.tables where table_schema='ged' and table_name in ('label_template_design','label_template_design_version','label_template_design_event')",cancellationToken:ct));Check(tables==3,"Tabelas PostgreSQL","As três tabelas não estão disponíveis.");
                var indexes=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from pg_indexes where schemaname='ged' and indexname in ('ix_label_template_design_key','ix_label_template_design_json_gin','ux_label_template_design_version_no')",cancellationToken:ct));Check(indexes==3,"Índices PostgreSQL","Índices obrigatórios ausentes.");
                var seeds=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(distinct template_key) from ged.label_template_design where template_key=any(@keys) and jsonb_typeof(design_json)='object'",new{keys=Seeds},cancellationToken:ct));Check(seeds==Seeds.Length,"Templates canvas no banco","Seeds ausentes ou com JSON inválido.");
                var invalidPublished=await db.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from ged.label_template_design d where d.status='PUBLISHED' and d.template_kind<>'CLASSIC' and not exists(select 1 from ged.label_template_design_version v where v.template_design_id=d.id and v.version_no=d.current_version)",cancellationToken:ct));Check(invalidPublished==0,"Versões publicadas","Há template publicado sem snapshot de versão.");
            }
            catch(Exception exception){warnings.Add($"Banco não verificável: {exception.GetType().Name}: {exception.Message}");}
        }
        foreach(var warning in warnings)output.WriteLine($"[AVISO] {warning}");output.WriteLine($"Labels Canvas Designer: {failures.Count} falha(s), {warnings.Count} aviso(s).");return failures.Count==0?0:2;
    }

    private static string Read(string root,params string[] path){var full=Path.Combine([root,..path]);return File.Exists(full)?File.ReadAllText(full):"";}
}
