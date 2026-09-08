using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InovaGed.Application.Labels.Canvas;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasRenderService(ILogger<LabelCanvasRenderService> logger) : ILabelCanvasRenderService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> Types = ["text","field","qr","barcode","logo","image","line","rect","group","table","separator"];

    public string ComputeSnapshotHash(string designJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(designJson ?? ""))).ToLowerInvariant();

    public LabelCanvasValidationResult Validate(string designJson, IReadOnlySet<string>? allowedFields = null)
    {
        var result = new LabelCanvasValidationResult();
        LabelCanvasDocumentDto? document;
        try { document = JsonSerializer.Deserialize<LabelCanvasDocumentDto>(designJson, Json); }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "JSON de design de etiqueta inválido.");
            result.Issues.Add(new("INVALID_JSON", "ERROR", "O layout contém JSON inválido."));
            return result;
        }
        if (document is null) { result.Issues.Add(new("EMPTY_DESIGN", "ERROR", "O layout está vazio.")); return result; }
        if (document.SchemaVersion < 1) result.Issues.Add(new("SCHEMA_VERSION", "ERROR", "O template deve informar uma versão de esquema válida."));
        if (string.IsNullOrWhiteSpace(document.Bindings.SubjectType)) result.Issues.Add(new("SUBJECT_TYPE", "ERROR", "O tipo de assunto é obrigatório."));
        if (document.Canvas.WidthMm <= 0 || document.Canvas.HeightMm <= 0) result.Issues.Add(new("CANVAS_SIZE", "ERROR", "As dimensões do canvas devem ser maiores que zero."));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || !ids.Add(element.Id)) result.Issues.Add(new("ELEMENT_ID", "ERROR", "Cada elemento deve ter um identificador único.", element.Id));
            if (string.IsNullOrWhiteSpace(element.Name)) result.Issues.Add(new("ELEMENT_NAME", "ERROR", "Informe um nome amigável para o elemento.", element.Id));
            if (!Types.Contains(element.Type)) result.Issues.Add(new("ELEMENT_TYPE", "ERROR", "O tipo do elemento não é suportado.", element.Id));
            if (element.WidthMm <= 0 || element.HeightMm <= 0) result.Issues.Add(new("ELEMENT_SIZE", "ERROR", "Largura e altura devem ser maiores que zero.", element.Id));
            if (element.XMm < 0 || element.YMm < 0 || element.XMm + element.WidthMm > document.Canvas.WidthMm || element.YMm + element.HeightMm > document.Canvas.HeightMm)
                result.Issues.Add(new("OUTSIDE_CANVAS", "ERROR", "O elemento está fora do canvas.", element.Id));
            var margin = document.Canvas.SafeMarginMm;
            if (element.XMm < margin || element.YMm < margin || element.XMm + element.WidthMm > document.Canvas.WidthMm - margin || element.YMm + element.HeightMm > document.Canvas.HeightMm - margin)
                result.Issues.Add(new("SAFE_MARGIN", "WARNING", "O elemento invade a margem de segurança.", element.Id));
            if (element.Style.FontSizePt is > 0 and < 6) result.Issues.Add(new("FONT_SIZE", "WARNING", "A fonte pode ficar ilegível na impressão.", element.Id));
            if (element.Validation.Required && element.Type == "text" && string.IsNullOrWhiteSpace(element.Text)) result.Issues.Add(new("REQUIRED_TEXT", "ERROR", "O texto obrigatório está vazio.", element.Id));
            if (element.Validation.MaxCharacters is int maximum && (element.Text?.Length ?? 0) > maximum) result.Issues.Add(new("MAX_CHARACTERS", "ERROR", $"O texto ultrapassa o limite de {maximum} caracteres.", element.Id));
            if (element.Type == "text" && !string.IsNullOrWhiteSpace(element.Text) && TextMayOverflow(element)) result.Issues.Add(new("TEXT_OVERFLOW", "WARNING", "O texto pode ser cortado na área configurada.", element.Id));
            if (element.Type is "field" or "qr" && string.IsNullOrWhiteSpace(element.Binding?.Field)) result.Issues.Add(new("FIELD_BINDING", "ERROR", "Selecione um campo dinâmico.", element.Id));
            if (element.Type == "qr" && string.IsNullOrWhiteSpace(element.Binding?.Field) && string.IsNullOrWhiteSpace(element.Text)) result.Issues.Add(new("QR_PAYLOAD", "ERROR", "O QR Code não possui conteúdo.", element.Id));
            if (allowedFields is not null && !string.IsNullOrWhiteSpace(element.Binding?.Field) && !allowedFields.Contains(element.Binding.Field)) result.Issues.Add(new("UNKNOWN_FIELD", "ERROR", "O campo dinâmico não existe no catálogo selecionado.", element.Id));
            if (element.Type is "logo" or "image" && !string.IsNullOrWhiteSpace(element.Binding?.Asset) && !IsSafeImageSource(element.Binding.Asset)) result.Issues.Add(new("INVALID_ASSET", "ERROR", "A imagem vinculada não possui uma origem válida.", element.Id));
        }
        for (var i = 0; i < document.Elements.Count; i++)
        for (var j = i + 1; j < document.Elements.Count; j++)
        {
            var a=document.Elements[i]; var b=document.Elements[j];
            if (!a.Visible || !b.Visible || a.Type is "line" or "rect" or "separator" || b.Type is "line" or "rect" or "separator") continue;
            var overlapW=Math.Min(a.XMm+a.WidthMm,b.XMm+b.WidthMm)-Math.Max(a.XMm,b.XMm);
            var overlapH=Math.Min(a.YMm+a.HeightMm,b.YMm+b.HeightMm)-Math.Max(a.YMm,b.YMm);
            if(overlapW>0&&overlapH>0&&overlapW*overlapH>Math.Min(a.WidthMm*a.HeightMm,b.WidthMm*b.HeightMm)*0.45m)
                result.Issues.Add(new("CRITICAL_OVERLAP","WARNING",$"Os elementos “{a.Name}” e “{b.Name}” têm sobreposição relevante.",a.Id));
        }
        return result;
    }

    public LabelCanvasRenderResult Render(LabelCanvasDesignDto design, IReadOnlyDictionary<string, object?> values, bool printMode = false)
    {
        var validation=Validate(design.DesignJson);
        LabelCanvasDocumentDto document;
        try { document=JsonSerializer.Deserialize<LabelCanvasDocumentDto>(design.DesignJson,Json) ?? new(); }
        catch(JsonException) { document=new(); }
        var offsetX=DecimalValue(values,"__offsetXmm",0);var offsetY=DecimalValue(values,"__offsetYmm",0);var scale=DecimalValue(values,"__scalePercent",100)/100m;
        var marginTop=DecimalValue(values,"__marginTopMm",0);var marginLeft=DecimalValue(values,"__marginLeftMm",0);var gapX=DecimalValue(values,"__gapXmm",4);var gapY=DecimalValue(values,"__gapYmm",4);
        var html=new StringBuilder();
        html.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>")
            .Append(WebUtility.HtmlEncode(design.TemplateName)).Append("</title><style>")
            .Append("*{box-sizing:border-box}html,body{margin:0;background:#eef1f6;font-family:Arial,sans-serif}.label-sheet{align-content:start;display:grid;gap:").Append(Mm(gapY)).Append("mm ").Append(Mm(gapX)).Append("mm;justify-content:center;min-height:100vh;padding:12mm}.label-sheet.two-up{grid-template-columns:repeat(2,max-content)}.label-canvas-output{background:#fff;position:relative;overflow:hidden;box-shadow:0 4mm 12mm #1a243322;transform:translate(").Append(Mm(offsetX)).Append("mm,").Append(Mm(offsetY)).Append("mm) scale(").Append(Mm(scale)).Append(");transform-origin:top left}.label-element{position:absolute;overflow:hidden;white-space:pre-wrap}.label-qr svg{width:100%;height:100%;display:block}.label-barcode{background:repeating-linear-gradient(90deg,#111 0,#111 1px,#fff 1px,#fff 3px)}@media print{@page{size:A4 portrait;margin:0}.label-sheet{padding:").Append(Mm(marginTop)).Append("mm 0 0 ").Append(Mm(marginLeft)).Append("mm;background:#fff;min-height:0}.label-canvas-output{box-shadow:none;break-inside:avoid}}")
            .Append("</style></head><body>");
        var copies=values.TryGetValue("__copies",out var rawCopies)&&int.TryParse(Convert.ToString(rawCopies),out var requested)?Math.Clamp(requested,1,100):1;
        html.Append("<main class=\"label-sheet").Append(document.Canvas.WidthMm<=100?" two-up":"").Append("\">");
        for(var copy=0;copy<copies;copy++)
        {
            html.Append("<section class=\"label-canvas-output\" data-template-key=\"").Append(WebUtility.HtmlEncode(design.TemplateKey)).Append("\" data-snapshot-hash=\"").Append(ComputeSnapshotHash(design.DesignJson))
                .Append("\" style=\"width:").Append(Mm(document.Canvas.WidthMm)).Append("mm;height:").Append(Mm(document.Canvas.HeightMm)).Append("mm\">");
            foreach(var element in document.Elements.Where(x=>x.Visible).OrderBy(x=>x.ZIndex))AppendElement(html,element,values);
            html.Append("</section>");
        }
        html.Append("</main>");
        if(printMode) html.Append("<script>addEventListener('load',()=>window.print())</script>");
        html.Append("</body></html>");
        var rendered=html.ToString().Replace("src=\"\"",string.Empty,StringComparison.OrdinalIgnoreCase).Replace("src=\"data:,\"",string.Empty,StringComparison.OrdinalIgnoreCase).Replace("src=\"/data:,\"",string.Empty,StringComparison.OrdinalIgnoreCase);
        return new(rendered,ComputeSnapshotHash(design.DesignJson),validation);
    }

    private static void AppendElement(StringBuilder html,LabelCanvasElementDto e,IReadOnlyDictionary<string,object?> values)
    {
        var style=$"left:{Mm(e.XMm)}mm;top:{Mm(e.YMm)}mm;width:{Mm(e.WidthMm)}mm;height:{Mm(e.HeightMm)}mm;transform:rotate({Mm(e.RotationDeg)}deg);z-index:{e.ZIndex};font-family:{SafeFont(e.Style.FontFamily)};font-size:{Mm(e.Style.FontSizePt)}pt;font-weight:{SafeWeight(e.Style.FontWeight)};text-align:{SafeAlign(e.Style.Align)};color:{SafeColor(e.Style.Color,"#111111")};background:{SafeColor(e.Style.BackgroundColor,"transparent")};border:{SafeBorder(e.Style.Border)};border-radius:{Mm(e.Style.BorderRadiusMm)}mm;padding:{Mm(e.Style.PaddingMm)}mm;";
        var value=ResolveValue(e,values);
        if(e.Validation.ShowOnlyWhenValue&&string.IsNullOrWhiteSpace(value))return;
        html.Append("<div class=\"label-element label-").Append(WebUtility.HtmlEncode(e.Type)).Append("\" data-element-id=\"").Append(WebUtility.HtmlEncode(e.Id)).Append("\" style=\"").Append(style).Append("\">");
        if(e.Type=="qr") html.Append(BuildQrSvg(value));
        else if(e.Type=="barcode") html.Append("<div class=\"label-barcode\" style=\"height:70%\"></div><small>").Append(WebUtility.HtmlEncode(value)).Append("</small>");
        else if(e.Type is "logo" or "image")
        {
            var source=!string.IsNullOrWhiteSpace(e.Binding?.Field)?value:e.Binding?.Asset;
            if(IsSafeImageSource(source)) html.Append("<img alt=\"").Append(WebUtility.HtmlEncode(e.Name)).Append("\" src=\"").Append(WebUtility.HtmlEncode(source)).Append("\" style=\"width:100%;height:100%;object-fit:contain\">");
            else html.Append("<strong>").Append(WebUtility.HtmlEncode(e.Binding?.Fallback ?? e.Name)).Append("</strong>");
        }
        else if(e.Type=="line"||e.Type=="separator") html.Append("<span style=\"display:block;border-top:1px solid currentColor;margin-top:50%\"></span>");
        else html.Append(WebUtility.HtmlEncode(value));
        html.Append("</div>");
    }

    private static string ResolveValue(LabelCanvasElementDto e,IReadOnlyDictionary<string,object?> values)
    {
        var field=e.Binding?.Field;
        if(!string.IsNullOrWhiteSpace(field)&&values.TryGetValue(field,out var value)&&value is not null) return Convert.ToString(value,CultureInfo.CurrentCulture)??"";
        return e.Text??e.Binding?.Fallback??"";
    }

    private static decimal DecimalValue(IReadOnlyDictionary<string,object?> values,string key,decimal fallback)=>values.TryGetValue(key,out var raw)&&decimal.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Number,CultureInfo.InvariantCulture,out var value)?value:fallback;

    private static string BuildQrSvg(string payload)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload ?? string.Empty, QRCodeGenerator.ECCLevel.Q);
        return new SvgQRCode(data).GetGraphic(4);
    }

    private static bool IsSafeImageSource(string? source) => !string.IsNullOrWhiteSpace(source) && source is not "data:," and not "/data:," &&
        (source.StartsWith("/",StringComparison.Ordinal) || (source.StartsWith("data:image/",StringComparison.OrdinalIgnoreCase)&&source.Contains(";base64,",StringComparison.OrdinalIgnoreCase)&&source.Length>40));
    private static bool TextMayOverflow(LabelCanvasElementDto element)
    {
        var availableWidth=Math.Max(1m,element.WidthMm-(element.Style.PaddingMm*2));var availableHeight=Math.Max(1m,element.HeightMm-(element.Style.PaddingMm*2));
        var characterWidth=Math.Max(0.5m,element.Style.FontSizePt*0.3528m*0.52m);var lineHeight=Math.Max(1m,element.Style.FontSizePt*0.3528m*1.2m);
        if(!element.Style.Wrap)return (element.Text?.Length??0)*characterWidth>availableWidth;
        var charactersPerLine=Math.Max(1,(int)Math.Floor(availableWidth/characterWidth));var lines=(int)Math.Ceiling((element.Text?.Length??0)/(decimal)charactersPerLine);
        return lines*lineHeight>availableHeight;
    }
    private static string Mm(decimal value)=>value.ToString("0.##",CultureInfo.InvariantCulture);
    private static string SafeFont(string value)=>value is "Arial" or "Helvetica" or "Verdana" or "Times New Roman" ? value : "Arial";
    private static string SafeAlign(string value)=>value is "left" or "center" or "right" or "justify" ? value : "left";
    private static string SafeWeight(string value)=>value is "400" or "500" or "600" or "700" or "bold" ? value : "400";
    private static string SafeColor(string value,string fallback)=>value=="transparent"||System.Text.RegularExpressions.Regex.IsMatch(value??"", "^#[0-9a-fA-F]{6}$")?value??fallback:fallback;
    private static string SafeBorder(string value)=>value is "none" or "1px solid #111111" or "1px dashed #111111" or "2px solid #111111" ? value : "none";
}
