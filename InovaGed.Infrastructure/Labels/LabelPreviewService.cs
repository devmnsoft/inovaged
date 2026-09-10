using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Labels.Preview;

namespace InovaGed.Infrastructure.Labels;

public sealed class LabelPreviewService(
    IDbConnectionFactory dbFactory,
    ILabelCanvasDesignService designs,
    ILabelCanvasPrintCoordinator canvasCoordinator,
    IManualLabelInstanceService manualLabels) : ILabelPreviewService
{
    public async Task<LabelQuickPreviewResult> GenerateQuickPreviewAsync(
        LabelQuickPreviewCommand command,
        CancellationToken cancellationToken = default)
    {
        var subjectType = (command.SubjectType ?? "").Trim().ToUpperInvariant();
        var templateCode = (command.TemplateCode ?? "").Trim();

        if (string.IsNullOrWhiteSpace(templateCode))
            return new(false, ErrorCode: "INVALID_TEMPLATE", Message: "Selecione um modelo de etiqueta.");

        if (string.IsNullOrWhiteSpace(subjectType))
            return new(false, ErrorCode: "INVALID_SOURCE", Message: "Selecione uma finalidade válida.");

        var isManual = subjectType == "MANUAL_LABEL";
        if (!isManual && command.SubjectId is null)
            return new(false, ErrorCode: "INVALID_SOURCE", Message: "Selecione a origem da etiqueta.");

        var design = await designs.GetPublishedAsync(command.TenantId, templateCode, null, cancellationToken);
        if (design is null)
            return new(false, ErrorCode: "INVALID_TEMPLATE", Message: "Modelo Canvas publicado não encontrado.");

        var manualValues = command.ManualValues ?? new Dictionary<string, string?>();
        if (isManual)
        {
            var validation = manualLabels.Validate(design, manualValues);
            if (validation.HasErrors)
            {
                var first = validation.Issues.First();
                return new(false,
                    ErrorCode: first.Code == "MANUAL_UNKNOWN_FIELD" ? "INVALID_TEMPLATE" : "MISSING_MANUAL_FIELD",
                    Message: first.Message,
                    Warnings: validation.Issues.Select(x => x.Message).ToArray());
            }
        }

        var subjectId = command.SubjectId ?? Guid.NewGuid();
        var resolvedManualValues = isManual
            ? manualValues.ToDictionary(x => x.Key, x => (object?)x.Value, StringComparer.OrdinalIgnoreCase)
            : null;

        var context = new LabelCanvasPrintContext
        {
            ExecutionMode = LabelCanvasExecutionMode.Preview,
            TenantId = command.TenantId,
            TemplateKey = templateCode,
            OperationalSubjectType = subjectType,
            SubjectId = subjectId,
            BrandingProfileId = command.BrandingProfileId,
            PrintProfileId = command.PrintProfileId,
            SelectedLogoAssetId = command.SelectedLogoAssetId,
            Copies = Math.Max(1, command.Copies),
            RegisterTrace = false,
            ReprintReason = command.ReprintReason,
            AbsoluteBaseUrl = command.AbsoluteBaseUrl,
            ResolvedValues = resolvedManualValues
        };

        LabelCanvasPreparedRender prepared;
        try
        {
            prepared = await canvasCoordinator.PrepareAsync(context, false, cancellationToken);
        }
        catch (KeyNotFoundException ex)
        {
            return new(false, ErrorCode: "INVALID_SOURCE", Message: ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new(false, ErrorCode: "INVALID_TEMPLATE", Message: ex.Message);
        }

        var sheet = LabelCanvasSheetLayoutCalculator.Calculate(
            prepared.Design.PaperKind,
            prepared.Design.Orientation,
            prepared.Design.WidthMm,
            prepared.Design.HeightMm);

        var labelsPerPage = Math.Max(1, sheet.LabelsPerPage);
        var estimatedPages = (int)Math.Ceiling((double)Math.Max(1, command.Copies) / labelsPerPage);

        var layout = new LabelPreviewLayoutInfo(
            prepared.Design.WidthMm,
            prepared.Design.HeightMm,
            prepared.Design.PaperKind,
            prepared.Design.Orientation,
            sheet.Columns,
            sheet.Rows,
            labelsPerPage,
            estimatedPages);

        var wasPrinted = false;
        if (command.SubjectId.HasValue && !isManual)
        {
            await using var db = await dbFactory.OpenAsync(cancellationToken);
            wasPrinted = await db.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from ged.label_print_history where tenant_id=@tid and label_subject_type=@type and label_subject_id=@id)",
                new { tid = command.TenantId, type = subjectType, id = command.SubjectId.Value },
                cancellationToken: cancellationToken));
        }

        var reprint = new LabelPreviewReprintInfo(wasPrinted, wasPrinted && string.IsNullOrWhiteSpace(command.ReprintReason));

        return new(
            Ok: true,
            Html: prepared.Html,
            Warnings: prepared.Validation.Issues.Where(x => x.Severity == "WARNING").Select(x => x.Message).ToArray(),
            Template: new(prepared.Design.TemplateKey, prepared.Design.TemplateName, prepared.Design.CurrentVersion.ToString(), prepared.Design.SubjectType),
            Branding: new(prepared.Branding?.ProfileId, prepared.Branding?.ProfileName, prepared.Branding?.ClientName, prepared.Branding?.ContractName, prepared.Branding?.OrganizationName),
            Layout: layout,
            Reprint: reprint);
    }
}
