using InovaGed.Application.Labels.Canvas;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelCanvasStarterTemplateService(ILabelCanvasFieldCatalogService fields) : ILabelCanvasStarterTemplateService
{
    public LabelCanvasDocumentDto Create(LabelCanvasStarterRequest request)
    {
        if (request.WidthMm is < 20 or > 500 || request.HeightMm is < 20 or > 500)
            throw new ArgumentOutOfRangeException(nameof(request), "O tamanho deve estar entre 20 e 500 mm.");
        if (!LabelPaperOptions.IsSupported(request.PaperKind))
            throw new ArgumentException("Papel não suportado.", nameof(request));

        var available = fields.GetFields(request.SubjectType).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var document = new LabelCanvasDocumentDto
        {
            Canvas = new LabelCanvasSettingsDto
            {
                WidthMm = request.WidthMm,
                HeightMm = request.HeightMm,
                Paper = request.PaperKind,
                GridMm = 2,
                SafeMarginMm = 3
            },
            Bindings = new LabelCanvasBindingsDto
            {
                SubjectType = request.SubjectType,
                SampleDataProfile = request.SubjectType.Equals("ManualLabel", StringComparison.OrdinalIgnoreCase) ? "ManualLabel" : "GENERIC"
            }
        };

        if (request.StarterKind == LabelCanvasStarterKind.Blank)
            return document;

        var calculated = LabelCanvasStarterLayoutCalculator.Calculate(
            request.WidthMm,
            request.HeightMm,
            request.SubjectType,
            request.StarterKind,
            available,
            safeMarginMm: 3m);

        var z = 0;
        foreach (var item in calculated)
        {
            document.Elements.Add(new LabelCanvasElementDto
            {
                Id = $"starter-{++z}",
                Type = item.ElementType,
                Name = item.Label,
                XMm = item.XMm,
                YMm = item.YMm,
                WidthMm = Math.Max(4, item.WidthMm),
                HeightMm = Math.Max(2, item.HeightMm),
                ZIndex = z,
                Text = item.Text,
                Style = new LabelCanvasStyleDto
                {
                    FontSizePt = item.FontSizePt,
                    FontFamily = "Arial",
                    FontWeight = item.Key == "controlNumber" ? "700" : "400"
                },
                Binding = item.BindingField is null ? null : new LabelCanvasBindingDto { Field = item.BindingField },
                Validation = new LabelCanvasElementValidationDto { Required = item.Required }
            });
        }

        return document;
    }
}
