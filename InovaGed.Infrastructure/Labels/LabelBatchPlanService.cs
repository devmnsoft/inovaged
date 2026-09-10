using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Batch;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Labels.Preview;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelBatchPlanService(
    IDbConnectionFactory dbFactory,
    ILabelCanvasDesignService designs,
    ILabelCanvasPrintCoordinator canvasCoordinator) : ILabelBatchPlanService
{
    public async Task<LabelBatchPlanResult> CalculatePlanAsync(
        LabelBatchPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var subjectType = (request.SubjectType ?? "").Trim().ToUpperInvariant();
        var templateCode = (request.TemplateCode ?? "").Trim();

        if (string.IsNullOrWhiteSpace(templateCode))
            return ErrorResult("Selecione um modelo de etiqueta.");

        if (request.SubjectIds == null || request.SubjectIds.Count == 0)
            return ErrorResult("Selecione ao menos um registro para impressão.");

        var design = await designs.GetPublishedAsync(request.TenantId, templateCode, null, cancellationToken);
        if (design is null)
            return ErrorResult("Modelo Canvas publicado não encontrado.");

        var sheet = LabelCanvasSheetLayoutCalculator.Calculate(
            design.PaperKind,
            design.Orientation,
            design.WidthMm,
            design.HeightMm);

        var labelsPerPage = Math.Max(1, sheet.LabelsPerPage);
        var copies = Math.Max(1, request.Copies);
        var physicalLabels = request.SubjectIds.Count * copies;
        var pageCount = (int)Math.Ceiling((double)physicalLabels / labelsPerPage);

        var layout = new LabelPreviewLayoutInfo(
            design.WidthMm,
            design.HeightMm,
            design.PaperKind,
            design.Orientation,
            sheet.Columns,
            sheet.Rows,
            labelsPerPage,
            pageCount);

        await using var db = await dbFactory.OpenAsync(cancellationToken);
        var printedRows = await db.QueryAsync<Guid>(new CommandDefinition("""
select distinct label_subject_id
from ged.label_print_history
where tenant_id = @TenantId
  and label_subject_type = @subjectType
  and label_subject_id = any(@ids)
""", new { request.TenantId, subjectType, ids = request.SubjectIds.ToArray() }, cancellationToken: cancellationToken));

        var printedSet = printedRows.ToHashSet();
        var alreadyPrintedCount = request.SubjectIds.Count(id => printedSet.Contains(id));

        var warnings = new List<string>();
        if (alreadyPrintedCount > 0)
        {
            warnings.Add($"{alreadyPrintedCount} de {request.SubjectIds.Count} etiquetas já foram emitidas anteriormente.");
            if (string.IsNullOrWhiteSpace(request.ReprintReason))
                warnings.Add("Informe o motivo da reimpressão antes de confirmar.");
        }

        var samples = new List<LabelBatchSamplePreview>();
        var itemErrors = new List<LabelBatchItemError>();

        var sampleIds = request.SubjectIds.Take(3).ToList();
        foreach (var sampleId in sampleIds)
        {
            var context = new LabelCanvasPrintContext
            {
                ExecutionMode = LabelCanvasExecutionMode.Preview,
                TenantId = request.TenantId,
                TemplateKey = templateCode,
                OperationalSubjectType = subjectType,
                SubjectId = sampleId,
                BrandingProfileId = request.BrandingProfileId,
                PrintProfileId = request.PrintProfileId,
                Copies = 1,
                RegisterTrace = false,
                AbsoluteBaseUrl = request.AbsoluteBaseUrl
            };

            try
            {
                var prepared = await canvasCoordinator.PrepareAsync(context, false, cancellationToken);
                var reference = ResolveSampleReference(prepared.Values, sampleId);
                samples.Add(new(sampleId, reference, prepared.Html));
            }
            catch (Exception ex)
            {
                var reference = $"Item {sampleId}";
                itemErrors.Add(new(sampleId, reference, ex.Message));
            }
        }

        return new LabelBatchPlanResult(
            Ok: itemErrors.Count == 0,
            SelectedCount: request.SubjectIds.Count,
            AlreadyPrintedCount: alreadyPrintedCount,
            PhysicalLabels: physicalLabels,
            LabelsPerPage: labelsPerPage,
            PageCount: pageCount,
            Layout: layout,
            Warnings: warnings,
            Samples: samples,
            ItemErrors: itemErrors);
    }

    private static string ResolveSampleReference(IReadOnlyDictionary<string, object?> values, Guid id)
    {
        var boxNo = values.GetValueOrDefault("boxNumber") ?? values.GetValueOrDefault("boxCode");
        if (boxNo is not null) return $"Caixa {boxNo}";

        var docCode = values.GetValueOrDefault("documentCode") ?? values.GetValueOrDefault("documentTitle");
        if (docCode is not null) return $"Doc {docCode}";

        var ctrl = values.GetValueOrDefault("controlNumber");
        if (ctrl is not null) return $"Controle {ctrl}";

        return $"Item {id.ToString()[..8]}";
    }

    private static LabelBatchPlanResult ErrorResult(string message) => new(
        Ok: false,
        SelectedCount: 0,
        AlreadyPrintedCount: 0,
        PhysicalLabels: 0,
        LabelsPerPage: 1,
        PageCount: 0,
        Layout: new(100, 70, "A4", "portrait", 1, 1, 1, 0),
        Warnings: Array.Empty<string>(),
        Samples: Array.Empty<LabelBatchSamplePreview>(),
        ItemErrors: Array.Empty<LabelBatchItemError>(),
        ErrorCode: "INVALID_BATCH_PLAN",
        Message: message);
}
