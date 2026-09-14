using System.Text.Json;
using System.Text.Json.Nodes;
using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelTemplatePackageService(ILabelCanvasRenderService renderer) : ILabelTemplatePackageService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive=true };
    private static readonly HashSet<string> Subjects = ["Box","Document","Folder","MedicalRecord","Process","ManualLabel"];

    public LabelTemplatePackage Export(LabelCanvasDesignDto design)
    {
        var document=ParseDocument(design.DesignJson);
        Sanitize(document);
        return new(1,document.SchemaVersion,new(design.TemplateName,design.Description,design.SubjectType,design.PaperKind,
            design.WidthMm,design.HeightMm,design.Orientation),document);
    }

    public LabelTemplatePackage Validate(string packageJson, ILabelCanvasFieldCatalogService fields)
    {
        if(string.IsNullOrWhiteSpace(packageJson)||packageJson.Length>1_000_000)throw Invalid("O pacote deve ter no máximo 1 MB.");
        LabelTemplatePackage package;
        try{var raw=JsonNode.Parse(packageJson)??throw new JsonException();RejectUnsafe(raw);package=JsonSerializer.Deserialize<LabelTemplatePackage>(packageJson,Json) ?? throw new JsonException();}
        catch(JsonException){throw Invalid("O arquivo não é um pacote de modelo válido.");}
        if(package.PackageVersion!=1)throw Invalid("A versão do pacote não é suportada.");
        if(package.SchemaVersion is <1 or >2||package.Design.SchemaVersion is <1 or >2)throw Invalid("Este modelo usa uma versão futura ou inválida.");
        if(!Subjects.Contains(package.Template.SubjectType))throw Invalid("A finalidade do modelo não é suportada.");
        if(!LabelPaperOptions.IsSupported(package.Template.PaperKind))throw Invalid("O papel informado não é suportado.");
        if(package.Template.Orientation is not ("portrait" or "landscape"))throw Invalid("A orientação informada não é suportada.");
        if(package.Template.WidthMm is < LabelCanvasDimensionPolicy.MinWidthMm or > LabelCanvasDimensionPolicy.MaxWidthMm ||
           package.Template.HeightMm is < LabelCanvasDimensionPolicy.MinHeightMm or > LabelCanvasDimensionPolicy.MaxHeightMm)throw Invalid("As dimensões do modelo são inválidas.");
        Sanitize(package.Design);
        var allowed=fields.GetFields(package.Template.SubjectType).Select(x=>x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validation=renderer.Validate(JsonSerializer.Serialize(package.Design,Json),allowed);
        if(validation.HasErrors)throw new LabelCanvasRequestException("INVALID_TEMPLATE_PACKAGE",validation.Issues.First(x=>x.Severity=="ERROR").Code=="UNKNOWN_FIELD"?"Este modelo usa um campo que não existe neste ambiente.":validation.Issues.First(x=>x.Severity=="ERROR").Message);
        return package;
    }

    private static LabelCanvasDocumentDto ParseDocument(string json){try{return JsonSerializer.Deserialize<LabelCanvasDocumentDto>(json,Json)??throw new JsonException();}catch(JsonException){throw Invalid("O layout do modelo é inválido.");}}
    private static void Sanitize(LabelCanvasDocumentDto document)
    {
        foreach(var element in document.Elements){element.Id=string.IsNullOrWhiteSpace(element.Id)?$"element-{Guid.NewGuid():N}":element.Id;element.GroupId=null;
            if(IsUnsafe(element.Text)||IsUnsafe(element.Binding?.Asset)||IsUnsafe(element.Binding?.Fallback)||IsUnsafe(element.Binding?.Field))throw Invalid("O modelo contém uma URL ou conteúdo externo não permitido.");
            if(ContainsMarkup(element.Text))throw Invalid("O modelo contém HTML executável não permitido.");
            if(element.Type=="logo"&&element.Binding is not null){var secondary=(element.Name+" "+element.Binding.Field).Contains("secondary",StringComparison.OrdinalIgnoreCase)||(element.Name+" "+element.Binding.Field).Contains("secund",StringComparison.OrdinalIgnoreCase);element.Binding.Asset=null;element.Binding.Field=secondary?"secondaryLogo":"primaryLogo";}}
    }
    private static bool IsUnsafe(string? value)=>!string.IsNullOrWhiteSpace(value)&&Uri.TryCreate(value.Trim(),UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https" or "file" or "javascript";
    private static bool ContainsMarkup(string? value)=>!string.IsNullOrWhiteSpace(value)&&(value.Contains("<script",StringComparison.OrdinalIgnoreCase)||value.Contains("<iframe",StringComparison.OrdinalIgnoreCase));
    private static void RejectUnsafe(JsonNode node){if(node is JsonValue value&&value.TryGetValue<string>(out var text)&&(IsUnsafe(text)||ContainsMarkup(text)))throw Invalid("O modelo contém uma URL ou conteúdo executável não permitido.");if(node is JsonObject obj)foreach(var child in obj.Select(x=>x.Value).Where(x=>x is not null))RejectUnsafe(child!);if(node is JsonArray array)foreach(var child in array.Where(x=>x is not null))RejectUnsafe(child!);}
    private static LabelCanvasRequestException Invalid(string message)=>new("INVALID_TEMPLATE_PACKAGE",message);
}
