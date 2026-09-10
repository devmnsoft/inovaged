using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasStarterTemplateService(ILabelCanvasFieldCatalogService fields) : ILabelCanvasStarterTemplateService
{
    public LabelCanvasDocumentDto Create(LabelCanvasStarterRequest request)
    {
        if (request.WidthMm is < 20 or > 500 || request.HeightMm is < 20 or > 500) throw new ArgumentOutOfRangeException(nameof(request), "O tamanho deve estar entre 20 e 500 mm.");
        if (!LabelPaperOptions.IsSupported(request.PaperKind)) throw new ArgumentException("Papel não suportado.", nameof(request));
        var available = fields.GetFields(request.SubjectType).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var document = new LabelCanvasDocumentDto
        {
            Canvas = new LabelCanvasSettingsDto { WidthMm=request.WidthMm, HeightMm=request.HeightMm, Paper=request.PaperKind, GridMm=2, SafeMarginMm=3 },
            Bindings = new LabelCanvasBindingsDto { SubjectType=request.SubjectType, SampleDataProfile="GENERIC" }
        };
        if (request.StarterKind == LabelCanvasStarterKind.Blank) return document;
        var z=0;
        Add("text","Título",3,3,request.WidthMm-6,9,null,"Etiqueta",ref z);
        AddField("headerTitle","Título institucional",3,13,request.WidthMm-6,8,ref z);
        if (request.StarterKind is LabelCanvasStarterKind.Institutional or LabelCanvasStarterKind.Traceability) AddField("primaryLogo","Logo",request.WidthMm-29,3,26,12,ref z,"logo");
        AddFirst(new[]{"boxCode","documentCode","recordNumber","processNumber","controlNumber"},"Código",3,24,request.WidthMm-6,9,ref z);
        AddFirst(new[]{"documentTitle","subject","patientName","boxNumber"},"Descrição",3,35,request.WidthMm-6,10,ref z);
        if (request.StarterKind is LabelCanvasStarterKind.Classification or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional)
        {
            AddField("classification","Classificação",3,47,request.WidthMm-34,8,ref z);
            AddField("location","Localização",3,56,request.WidthMm-34,8,ref z);
        }
        if (request.StarterKind is LabelCanvasStarterKind.IdentificationQr or LabelCanvasStarterKind.Traceability or LabelCanvasStarterKind.Institutional) AddField("qrPayload","QR",request.WidthMm-28,request.HeightMm-28,24,24,ref z,"qr");
        return document;

        void AddFirst(IEnumerable<string> keys,string name,decimal x,decimal y,decimal w,decimal h,ref int order)
        { var key=keys.FirstOrDefault(available.Contains); if(key is not null) AddField(key,name,x,y,w,h,ref order); }
        void AddField(string key,string name,decimal x,decimal y,decimal w,decimal h,ref int order,string type="field")
        { if(available.Contains(key)) Add(type,name,x,y,w,h,key,null,ref order); }
        void Add(string type,string name,decimal x,decimal y,decimal w,decimal h,string? binding,string? text,ref int order) => document.Elements.Add(new LabelCanvasElementDto
        { Id=$"starter-{++order}", Type=type, Name=name, XMm=x, YMm=y, WidthMm=Math.Max(4,w), HeightMm=Math.Max(2,h), ZIndex=order, Text=text,
          Binding=binding is null?null:new LabelCanvasBindingDto{Field=binding}, Validation=new LabelCanvasElementValidationDto{Required=binding is not null} });
    }
}
