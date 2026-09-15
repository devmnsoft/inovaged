using InovaGed.Application.Labels.Canvas;
using InovaGed.Application.Labels.Intelligence;

namespace InovaGed.Infrastructure.Labels;

/// <summary>
/// Read-only preflight orchestration. It deliberately reuses the canvas validator and runtime renderer,
/// so print and publish safety rules continue to have one authority.
/// </summary>
public sealed class LabelOperationalIntelligenceService(ILabelCanvasDesignService designs,
    ILabelCanvasValueResolver values, ILabelCanvasFieldCatalogService fields, ILabelCanvasRenderService renderer)
    : ILabelPreflightService
{
    public async Task<LabelPreflightResult> CheckAsync(LabelPreflightRequest request, CancellationToken cancellationToken = default)
    {
        if (request.TenantId == Guid.Empty || request.UserId == Guid.Empty)
            return Result(new("IDENTITY_REQUIRED", LabelPreflightSeverity.Error, "Acesso não confirmado", "Não foi possível confirmar o tenant e o usuário desta operação.", Category: LabelPreflightCategory.Subject));
        if (request.Copies is < 1 or > 500)
            return Result(new("COPIES_OUT_OF_RANGE", LabelPreflightSeverity.Error, "Quantidade inválida", "Escolha entre 1 e 500 cópias.", SuggestedAction: "print", Category: LabelPreflightCategory.Printing));

        var design = request.AllowDraftPreview
            ? await designs.GetAsync(request.TenantId, request.TemplateKey, cancellationToken)
            : await designs.GetPublishedAsync(request.TenantId, request.TemplateKey, cancellationToken: cancellationToken);
        if (design is null)
            return Result(new("TEMPLATE_UNAVAILABLE", LabelPreflightSeverity.Error, "Modelo indisponível", "Selecione um modelo publicado e acessível.", SuggestedAction: "template", Category: LabelPreflightCategory.Layout));

        IReadOnlyDictionary<string, object?> resolved;
        try
        {
            resolved = await values.ResolveAsync(request.TenantId, design.SubjectType,
                LabelCanvasSubjectTypeMapper.ToOperational(request.SubjectType), request.SubjectId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return Result(new("SUBJECT_UNAVAILABLE", LabelPreflightSeverity.Error, "Registro indisponível", "O registro não existe ou não está acessível para este usuário.", SuggestedAction: "subject", Category: LabelPreflightCategory.Subject));
        }

        var effective = string.IsNullOrWhiteSpace(request.DesignJson) ? design : CopyWithJson(design, request.DesignJson!);
        var allowed = fields.GetFields(effective.SubjectType).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validation = renderer.Validate(effective.DesignJson, allowed);
        var runtime = renderer.Render(effective, resolved).Validation;
        var issues = validation.Issues.Concat(runtime.Issues)
            .DistinctBy(x => (x.Code, x.ElementId, x.Message))
            .Select(ToItem).ToList();
        if (!string.IsNullOrWhiteSpace(request.PaperKind) && !request.PaperKind.Equals(effective.PaperKind, StringComparison.OrdinalIgnoreCase))
            issues.Add(new("PAPER_MISMATCH", LabelPreflightSeverity.Warning, "Papel diferente do modelo", $"O modelo foi preparado para {effective.PaperKind}, mas {request.PaperKind} foi selecionado.", SuggestedAction: "printer", Category: LabelPreflightCategory.Printing));
        if (request.CalibrationProfileId is null)
            issues.Add(new("CALIBRATION_RECOMMENDED", LabelPreflightSeverity.Recommendation, "Confira a calibração", "Use um perfil compatível com o tamanho da etiqueta antes de imprimir.", SuggestedAction: "calibration", Category: LabelPreflightCategory.Calibration));
        if (string.IsNullOrWhiteSpace(request.PrinterName))
            issues.Add(new("PRINTER_NOT_SELECTED", LabelPreflightSeverity.Information, "Impressora ainda não selecionada", "A impressora poderá ser escolhida na etapa de impressão.", SuggestedAction: "printer", Category: LabelPreflightCategory.Printing));
        return new LabelPreflightResult { Items = issues };
    }

    public async Task<IReadOnlyList<LabelPreflightResult>> CheckBatchAsync(IReadOnlyList<LabelPreflightRequest> requests, CancellationToken cancellationToken = default)
    {
        var detailed = await CheckBatchDetailedAsync(new(requests), cancellationToken: cancellationToken);
        return detailed.Items.Select(x => x.Result).ToArray();
    }

    public async Task<LabelBatchPreflightResult> CheckBatchDetailedAsync(LabelBatchPreflightRequest request,
        IProgress<LabelBatchPreflightProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Items.Count is < 1 || request.Items.Count > Math.Clamp(request.MaxSelection, 1, 500))
            throw new ArgumentOutOfRangeException(nameof(request), "A seleção deve respeitar o limite configurado.");
        var started = DateTimeOffset.UtcNow;
        var output = new LabelBatchPreflightItem[request.Items.Count];
        var completed = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, request.Items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(request.MaxConcurrency, 1, 8), CancellationToken = cancellationToken },
            async (index, ct) =>
            {
                var item = request.Items[index];
                var result = await CheckAsync(item, ct);
                var status = !result.CanPrint ? "BLOCKED" : result.Warnings.Count > 0 ? "READY_WITH_WARNING" : "READY";
                output[index] = new(item.SubjectId, status, result);
                progress?.Report(new(Interlocked.Increment(ref completed), request.Items.Count));
            });
        var blocked = output.Count(x => x.Status == "BLOCKED");
        var warnings = output.Count(x => x.Status == "READY_WITH_WARNING");
        var ready = output.Length - blocked - warnings;
        var readiness = output.Length == 0 ? 0 : (int)Math.Round((ready + warnings) * 100m / output.Length);
        return new(output, new(output.Length, ready, warnings, blocked, readiness,
            DateTimeOffset.UtcNow - started, DateTimeOffset.UtcNow));
    }

    private static LabelPreflightItem ToItem(LabelCanvasValidationIssue issue) => new(issue.Code,
        issue.Severity == "ERROR" ? LabelPreflightSeverity.Error : LabelPreflightSeverity.Warning,
        issue.Code == "TEXT_OVERFLOW" ? "Conteúdo não cabe nesta área" : issue.Severity == "ERROR" ? "Correção obrigatória" : "Atenção antes de imprimir",
        HumanMessage(issue),
        issue.ElementId, issue.ElementId is null ? "template" : "designer",
        issue.Code is "TEXT_OVERFLOW" or "SAFE_MARGIN", issue.Code == "TEXT_OVERFLOW" ? "safe-auto-fit" : issue.Code == "SAFE_MARGIN" ? "fit-safe-margin" : null,
        issue.Code is "TEXT_OVERFLOW" or "SAFE_MARGIN" or "LABEL_OVERFLOW" ? LabelPreflightCategory.Layout : LabelPreflightCategory.Content);
    private static string HumanMessage(LabelCanvasValidationIssue issue) => issue.Code switch
    {
        "FIELD_REQUIRED_EMPTY" => "Um campo obrigatório está vazio. Escolha outro registro ou complete o conteúdo.",
        "LABEL_OVERFLOW" => "O conteúdo ultrapassa a área da etiqueta.",
        "TEXT_OVERFLOW" => "O conteúdo não cabe nesta área. Reduza a fonte, aumente a área ou permita quebra de linha.",
        "SAFE_MARGIN" => "O elemento está fora da margem segura da etiqueta.",
        "CALIBRATION_MISMATCH" => "O perfil de calibração selecionado foi criado para outro tamanho.",
        _ => issue.Message
    };
    private static LabelPreflightResult Result(LabelPreflightItem item) => new() { Items = [item] };
    private static LabelCanvasDesignDto CopyWithJson(LabelCanvasDesignDto d, string json) => new()
    {
        Id=d.Id,TenantId=d.TenantId,TemplateKey=d.TemplateKey,TemplateName=d.TemplateName,Description=d.Description,
        TemplateKind=d.TemplateKind,SubjectType=d.SubjectType,PaperKind=d.PaperKind,WidthMm=d.WidthMm,HeightMm=d.HeightMm,
        Orientation=d.Orientation,Status=d.Status,DesignJson=json,CurrentVersion=d.CurrentVersion,IsSystemTemplate=d.IsSystemTemplate,
        DefaultBrandingProfileId=d.DefaultBrandingProfileId,BrandingBindingKey=d.BrandingBindingKey,LabelContext=d.LabelContext,
        LockVersion=d.LockVersion,HasPublishedVersion=d.HasPublishedVersion,PublishedVersionNo=d.PublishedVersionNo
    };
}

public sealed class LabelTemplateRecommendationService : ILabelTemplateRecommendationService
{
    // Documented deterministic weights: subject 35, required data 20, paper 15, published 10,
    // validation health 10, branding 5, calibration 3, successful tenant history 2.
    public IReadOnlyList<LabelTemplateRecommendation> Recommend(LabelTemplateRecommendationRequest request)
    {
        if (request.TenantId == Guid.Empty) throw new ArgumentException("Tenant obrigatório.", nameof(request));
        return request.Candidates.Select(c =>
        {
            var reasons=new List<string>(); var score=0;
            var subject=c.SubjectType.Equals(request.SubjectType,StringComparison.OrdinalIgnoreCase);
            if(subject){score+=35;reasons.Add("Foi criado para este tipo de registro.");}
            if(c.RequiredBindingsAvailable){score+=20;reasons.Add("Possui todos os campos obrigatórios disponíveis.");}
            if(string.IsNullOrWhiteSpace(request.PaperKind)||c.PaperKind.Equals(request.PaperKind,StringComparison.OrdinalIgnoreCase)){score+=15;reasons.Add("É compatível com o papel selecionado.");}
            if(c.Published){score+=10;reasons.Add("Possui versão publicada.");} if(c.Healthy){score+=10;reasons.Add("A validação do modelo está saudável.");}
            if(c.BrandingCompatible){score+=5;reasons.Add("É compatível com a identidade selecionada.");} if(c.CalibrationCompatible){score+=3;reasons.Add("Há calibração compatível.");}
            if(c.UsedSuccessfullyBefore){score+=2;reasons.Add("Foi usado anteriormente com sucesso neste tenant.");}
            var compatible=subject&&c.RequiredBindingsAvailable&&c.Published&&c.Healthy;
            return new LabelTemplateRecommendation(c.TemplateKey,Math.Clamp(score,0,100),compatible,reasons);
        }).OrderByDescending(x=>x.IsCompatible).ThenByDescending(x=>x.Score).ThenBy(x=>x.TemplateKey,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class LabelPrintProfileRecommendationService : ILabelPrintProfileRecommendationService
{
    public LabelPrintProfileRecommendation? Recommend(Guid tenantId,string paperKind,decimal widthMm,decimal heightMm,string? printerName,IReadOnlyList<LabelPrintProfileCandidate> profiles)
    {
        if(tenantId==Guid.Empty)throw new ArgumentException("Tenant obrigatório.",nameof(tenantId));
        return profiles.Select(p=>
        {
            var paper=p.PaperKind.Equals(paperKind,StringComparison.OrdinalIgnoreCase); var dimensions=Math.Abs(p.LabelWidthMm-widthMm)<=.1m&&Math.Abs(p.LabelHeightMm-heightMm)<=.1m;
            var printer=!string.IsNullOrWhiteSpace(printerName)&&p.PrinterName?.Equals(printerName,StringComparison.OrdinalIgnoreCase)==true;
            var compatible=paper&&dimensions; var score=compatible?(printer?100:80):(p.IsTenantDefault?30:0); if(compatible&&p.UsedSuccessfullyBefore)score+=5;
            var reason=compatible&&p.UsedSuccessfullyBefore?"Usado anteriormente com este modelo e tamanho.":printer&&compatible?"Impressora, papel e dimensões são compatíveis.":compatible?"Papel e dimensões são compatíveis.":"Perfil padrão do tenant.";
            return (p: p, score: Math.Min(score, 100), reason: reason, compatible: compatible);
        }).Where(x=>x.compatible||x.p.IsTenantDefault).OrderByDescending(x=>x.score).Select(x=>new LabelPrintProfileRecommendation(x.p.Id,x.p.Name,x.score,x.reason)).FirstOrDefault();
    }
}
