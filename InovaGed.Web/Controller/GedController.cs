using System.Globalization;
using System.Text;
using Dapper;
using InovaGed.Application;
using InovaGed.Application.Classification;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Common.Storage;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Identity;
using InovaGed.Application.Ocr;
using InovaGed.Application.Preview;
using InovaGed.Application.Search;
using InovaGed.Application.Workflow;
using InovaGed.Domain.Ged;
using InovaGed.Domain.Primitives;
using InovaGed.Web.Models.Ged;
using InovaGed.Web.ocr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace InovaGed.Web.Controllers;

[Authorize]
public sealed class GedController : Controller
{
    private readonly ILogger<GedController> _logger;
    private readonly ICurrentUser _currentUser;

    private readonly IDbConnectionFactory _db;

    private readonly IDocumentCommands _documentCommands;
    private readonly IOcrSignalRNotifier _ocrNotifier;

    private readonly IFolderQueries _folders;
    private readonly IFolderCommands _folderCommands;

    private readonly IOcrJobRepository _ocrJobs;
    private readonly IOcrStatusQueries _ocrStatus; // (mantido)

    private readonly IDocumentQueries _docs;
    private readonly DocumentAppService _documentApp;
    private readonly IFileStorage _storage;
    private readonly IPreviewGenerator _preview;
    private readonly IPreviewJobQueue _previewQueue;
    private readonly IPreviewStatusRepository _previewStatus;

    private readonly IDocumentSearchQueries _search;

    // ✅ classificação
    private readonly IDocumentClassificationQueries _clsQ;

    // Workflow
    private readonly IWorkflowQueries _workflowQ;
    private readonly IWorkflowCommands _workflowC;
    private readonly IDocumentWorkflowQueries _docWfQ;
    private readonly IDocumentWorkflowCommands _docWfC;

    public GedController(
        ILogger<GedController> logger,
        ICurrentUser currentUser,
        IDbConnectionFactory db,                    // ✅ ADICIONADO
        IFolderQueries folders,
        IOcrStatusQueries ocrStatus,
        IDocumentSearchQueries search,
        IOcrJobRepository ocrJobs,
        IFolderCommands folderCommands,
        IDocumentQueries docs,
        DocumentAppService documentApp,
        IFileStorage storage,
        IPreviewGenerator preview,
        IPreviewJobQueue previewQueue,
        IPreviewStatusRepository previewStatus,
        IWorkflowQueries workflowQ,
        IWorkflowCommands workflowC,
        IDocumentWorkflowQueries docWfQ,
        IDocumentWorkflowCommands docWfC,
        IDocumentClassificationQueries clsQ,
        IDocumentCommands documentCommands,
        IOcrSignalRNotifier ocrNotifier)
    {
        _logger = logger;
        _currentUser = currentUser;
        _db = db;

        _folders = folders;
        _folderCommands = folderCommands;

        _ocrJobs = ocrJobs;
        _ocrStatus = ocrStatus;

        _search = search;

        _docs = docs;
        _documentApp = documentApp;
        _storage = storage;
        _preview = preview;
        _previewQueue = previewQueue;
        _previewStatus = previewStatus;

        _workflowQ = workflowQ;
        _workflowC = workflowC;
        _docWfQ = docWfQ;
        _docWfC = docWfC;

        _clsQ = clsQ;

        _documentCommands = documentCommands;
        _ocrNotifier = ocrNotifier;
    }

    private bool IsAjaxRequest()
        => string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
           || (Request.Headers.TryGetValue("Accept", out var a) && a.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase));

    // =========================
    // EXPLORER
    // =========================
    [HttpGet]
    public async Task<IActionResult> Index(Guid? folderId, string? q, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            var tenantId = _currentUser.TenantId;

            var tree = await _folders.TreeAsync(tenantId, ct);

            // se não veio folderId, tenta apontar para a "primeira" pasta da árvore
            var effectiveFolderId = folderId ?? tree.FirstOrDefault()?.Id;

            var docs = effectiveFolderId.HasValue
                ? await _docs.ListAsync(tenantId, effectiveFolderId.Value, q, ct)
                : Array.Empty<DocumentRowDto>();

            // ✅ KPI: documentos não classificados (filtra por pasta quando houver)
            ViewBag.UnclassifiedCount = await _clsQ.CountUnclassifiedAsync(tenantId, effectiveFolderId, ct);
            ViewBag.RunningOcrCount = await CountRunningOcrJobsAsync(tenantId, ct);

            var vm = new GedExplorerVM
            {
                CurrentFolderId = effectiveFolderId,
                FolderId = effectiveFolderId,
                Query = q,
                Folders = tree.Select(x => new GedExplorerVM.FolderNodeVM
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    Level = x.Level
                }).ToList(),
                Documents = docs.Select(d => new GedExplorerVM.DocumentRowVM
                {
                    Id = d.Id,
                    Title = d.Title,
                    TypeName = d.TypeName,
                    FileName = d.FileName,
                    SizeBytes = d.SizeBytes,
                    CreatedAt = d.CreatedAt,
                    CreatedBy = d.CreatedBy,
                    IsConfidential = d.IsConfidential
                }).ToList()
            };

            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar Explorer. FolderId={FolderId} q={q}", folderId, q);
            TempData["erro"] = "Erro ao carregar Explorer.";
            return View(new GedExplorerVM());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Processing(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return RedirectToAction("Login", "Account");

        var tenantId = _currentUser.TenantId;
        var rows = await ListRunningOcrJobsAsync(tenantId, ct);
        ViewBag.ProcessingMetrics = await GetProcessingMetricsAsync(tenantId, ct);
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Kpi(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return RedirectToAction("Login", "Account");

        var tenantId = _currentUser.TenantId;

        const string sql = @"
SELECT
  (SELECT count(*)::int FROM ged.document d WHERE d.tenant_id = @tenantId AND d.reg_status = 'A') AS TotalDocuments,
  (SELECT count(*)::int FROM ged.document d WHERE d.tenant_id = @tenantId AND d.reg_status = 'A' AND d.created_at >= date_trunc('month', now())) AS DocumentsThisMonth,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'PROCESSING'::ged.ocr_status_enum) AS OcrProcessing,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'PENDING'::ged.ocr_status_enum) AS OcrPending,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'COMPLETED'::ged.ocr_status_enum AND j.finished_at >= now() - interval '24 hours') AS OcrCompleted24h,
  (SELECT count(*)::int FROM ged.ocr_job j WHERE j.tenant_id = @tenantId AND j.status = 'ERROR'::ged.ocr_status_enum AND j.finished_at >= now() - interval '24 hours') AS OcrErrors24h,
  (SELECT count(*)::int FROM ged.document d WHERE d.tenant_id = @tenantId AND d.reg_status = 'A' AND (d.description IS NULL OR btrim(d.description) = '' OR btrim(d.description) = '-')) AS DocumentsWithoutDescription;";

        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleAsync(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

        var vm = new GedKpiVm
        {
            TotalDocuments = (int)row.totaldocuments,
            DocumentsThisMonth = (int)row.documentsthismonth,
            OcrProcessing = (int)row.ocrprocessing,
            OcrPending = (int)row.ocrpending,
            OcrCompleted24h = (int)row.ocrcompleted24h,
            OcrErrors24h = (int)row.ocrerrors24h,
            DocumentsWithoutDescription = (int)row.documentswithoutdescription
        };

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> ProcessingStatus(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var rows = await ListRunningOcrJobsAsync(tenantId, ct);
        return Json(new { success = true, count = rows.Count, items = rows });
    }

    [HttpGet]
    public async Task<IActionResult> ProcessingMetrics(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;
        var metrics = await GetProcessingMetricsAsync(tenantId, ct);
        var recentErrors = await ListRecentOcrErrorsAsync(tenantId, ct);

        return Json(new { success = true, metrics, recentErrors });
    }

    [HttpGet]
    public async Task<IActionResult> QueueSnapshot(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Unauthorized();

        var tenantId = _currentUser.TenantId;

        const string sql = @"
WITH base AS (
  SELECT
    j.id AS job_id,
    j.document_version_id AS version_id,
    d.id AS document_id,
    d.title AS document_title,
    j.status::text AS status_text,
    j.requested_at,
    j.started_at
  FROM ged.ocr_job j
  JOIN ged.document_version dv
    ON dv.tenant_id = j.tenant_id
   AND dv.id = j.document_version_id
  JOIN ged.document d
    ON d.tenant_id = dv.tenant_id
   AND d.id = dv.document_id
  WHERE j.tenant_id = @tenantId
    AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
),
totals AS (
  SELECT
    count(*) FILTER (WHERE status_text = 'PENDING')::int AS pending_count,
    count(*) FILTER (WHERE status_text = 'PROCESSING')::int AS processing_count
  FROM base
)
SELECT
  b.job_id AS JobId,
  b.version_id AS VersionId,
  b.document_id AS DocumentId,
  b.document_title AS DocumentTitle,
  b.status_text AS Status,
  b.requested_at AS RequestedAt,
  b.started_at AS StartedAt,
  row_number() OVER (ORDER BY b.requested_at) AS QueuePosition,
  t.pending_count AS PendingCount,
  t.processing_count AS ProcessingCount
FROM base b
CROSS JOIN totals t
ORDER BY b.requested_at
LIMIT 50;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = (await conn.QueryAsync<QueueSnapshotRowVm>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct))).ToList();

        var pendingCount = rows.FirstOrDefault()?.PendingCount ?? 0;
        var processingCount = rows.FirstOrDefault()?.ProcessingCount ?? 0;

        return Json(new
        {
            success = true,
            pendingCount,
            processingCount,
            total = pendingCount + processingCount,
            items = rows
        });
    }

    private async Task<int> CountRunningOcrJobsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT count(1)
FROM ged.ocr_job j
WHERE j.tenant_id = @tenantId
  AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum);";

        await using var conn = await _db.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
    }

    private async Task<List<ProcessingRowVm>> ListRunningOcrJobsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  j.id AS JobId,
  j.document_version_id AS VersionId,
  d.id AS DocumentId,
  d.title AS DocumentTitle,
  j.status::text AS Status,
  j.requested_at AS RequestedAt,
  j.started_at AS StartedAt
FROM ged.ocr_job j
JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
JOIN ged.document d
  ON d.tenant_id = dv.tenant_id
 AND d.id = dv.document_id
WHERE j.tenant_id = @tenantId
  AND j.status IN ('PENDING'::ged.ocr_status_enum, 'PROCESSING'::ged.ocr_status_enum)
ORDER BY j.requested_at DESC
LIMIT 200;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessingRowVm>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    private async Task<ProcessingMetricsVm> GetProcessingMetricsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  count(*) FILTER (WHERE j.status = 'PENDING'::ged.ocr_status_enum)    AS PendingCount,
  count(*) FILTER (WHERE j.status = 'PROCESSING'::ged.ocr_status_enum) AS ProcessingCount,
  count(*) FILTER (WHERE j.status = 'ERROR'::ged.ocr_status_enum
                   AND j.requested_at >= now() - interval '24 hours')   AS Errors24h,
  coalesce(avg(extract(epoch from (j.started_at - j.requested_at)))
      FILTER (WHERE j.started_at IS NOT NULL
              AND j.requested_at >= now() - interval '24 hours'), 0)     AS AvgQueueSeconds
FROM ged.ocr_job j
WHERE j.tenant_id = @tenantId;";

        await using var conn = await _db.OpenAsync(ct);
        var row = await conn.QuerySingleAsync(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));

        return new ProcessingMetricsVm
        {
            PendingCount = (int)row.pendingcount,
            ProcessingCount = (int)row.processingcount,
            Errors24h = (int)row.errors24h,
            AvgQueueSeconds = Convert.ToDecimal(row.avgqueueseconds)
        };
    }

    private async Task<List<ProcessingErrorVm>> ListRecentOcrErrorsAsync(Guid tenantId, CancellationToken ct)
    {
        const string sql = @"
SELECT
  j.id AS JobId,
  d.id AS DocumentId,
  d.title AS DocumentTitle,
  j.error_message AS ErrorMessage,
  j.finished_at AS FinishedAt
FROM ged.ocr_job j
JOIN ged.document_version dv
  ON dv.tenant_id = j.tenant_id
 AND dv.id = j.document_version_id
JOIN ged.document d
  ON d.tenant_id = dv.tenant_id
 AND d.id = dv.document_id
WHERE j.tenant_id = @tenantId
  AND j.status = 'ERROR'::ged.ocr_status_enum
ORDER BY j.finished_at DESC NULLS LAST
LIMIT 20;";

        await using var conn = await _db.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProcessingErrorVm>(
            new CommandDefinition(sql, new { tenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    public sealed class ProcessingRowVm
    {
        public long JobId { get; set; }
        public Guid VersionId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime RequestedAt { get; set; }
        public DateTime? StartedAt { get; set; }
    }

    public sealed class ProcessingMetricsVm
    {
        public int PendingCount { get; set; }
        public int ProcessingCount { get; set; }
        public int Errors24h { get; set; }
        public decimal AvgQueueSeconds { get; set; }
    }

    public sealed class ProcessingErrorVm
    {
        public long JobId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    public sealed class QueueSnapshotRowVm
    {
        public long JobId { get; set; }
        public Guid VersionId { get; set; }
        public Guid DocumentId { get; set; }
        public string DocumentTitle { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime RequestedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public int QueuePosition { get; set; }
        public int PendingCount { get; set; }
        public int ProcessingCount { get; set; }
    }

    public sealed class GedKpiVm
    {
        public int TotalDocuments { get; set; }
        public int DocumentsThisMonth { get; set; }
        public int OcrProcessing { get; set; }
        public int OcrPending { get; set; }
        public int OcrCompleted24h { get; set; }
        public int OcrErrors24h { get; set; }
        public int DocumentsWithoutDescription { get; set; }
    }

    // =========================
    // DETAILS
    // =========================
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, Guid? versionId, bool? openClassify, bool? ocrSwitched, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            var tenantId = _currentUser.TenantId;

            var doc = await _docs.GetAsync(tenantId, id, ct);
            if (doc is null) return NotFound();

            var versions = await _docs.ListVersionsAsync(tenantId, id, ct);

            // ========= WORKFLOW =========
            var wfState = await _docWfQ.GetCurrentAsync(tenantId, id, ct);

            var wfVm = new GedDetailsVM.WorkflowVM
            {
                HasActiveWorkflow = wfState is not null,
                DocumentWorkflowId = wfState?.DocumentWorkflowId,
                WorkflowId = wfState?.WorkflowId,
                WorkflowName = wfState?.WorkflowName,
                CurrentStageId = wfState?.CurrentStageId,
                CurrentStageName = wfState?.CurrentStageName,
                IsCompleted = wfState?.IsCompleted ?? false,
                StartedAt = wfState?.StartedAt,
                StartedBy = wfState?.StartedBy
            };

            var defs = await _workflowQ.ListDefinitionsAsync(tenantId, q: null, ct);
            wfVm.AvailableWorkflows = defs
                .Where(x => x.IsActive)
                .Select(x => new GedDetailsVM.WorkflowVM.WorkflowDefinitionRow
                {
                    Id = x.Id,
                    Name = $"{x.Code} - {x.Name}"
                })
                .ToList();

            if (wfState is not null && !wfState.IsCompleted)
            {
                var transitions = await _docWfQ.ListAvailableTransitionsAsync(tenantId, id, ct);
                wfVm.AvailableTransitions = transitions.Select(t => new GedDetailsVM.WorkflowVM.TransitionRow
                {
                    Id = t.Id,
                    Name = t.Name,
                    ToStageId = t.ToStageId,
                    ToStageName = t.ToStageName,
                    RequiresReason = t.RequiresReason
                }).ToList();
            }

            var history = await _docWfQ.ListHistoryAsync(tenantId, id, ct);
            wfVm.History = history.Select(h => new GedDetailsVM.WorkflowVM.HistoryRow
            {
                Id = h.Id,
                FromStageName = h.FromStageName,
                ToStageName = h.ToStageName,
                PerformedAt = h.PerformedAt,
                PerformedBy = h.PerformedBy,
                Reason = h.Reason,
                Comments = h.Comments
            }).ToList();

            var vm = new GedDetailsVM
            {
                Id = doc.Id,
                FolderId = doc.FolderId,
                Title = doc.Title,
                Description = doc.Description,
                IsConfidential = doc.IsConfidential,
                CreatedBy = doc.CreatedBy,
                CurrentVersionId = doc.CurrentVersionId,
                Versions = versions.Select(v => new GedDetailsVM.VersionVM
                {
                    Id = v.Id,
                    VersionNumber = v.VersionNumber,
                    FileName = v.FileName,
                    ContentType = v.ContentType ?? "",
                    SizeBytes = v.SizeBytes,
                    CreatedAt = v.CreatedAt,
                    CreatedBy = v.CreatedBy,
                    IsCurrent = doc.CurrentVersionId.HasValue && v.Id == doc.CurrentVersionId.Value,

                    // OCR (se existir no DTO)
                    OcrStatus = v.OcrStatus,
                    OcrJobId = v.OcrJobId,
                    OcrErrorMessage = v.OcrErrorMessage,
                    OcrRequestedAt = v.OcrRequestedAt,
                    OcrStartedAt = v.OcrStartedAt,
                    OcrFinishedAt = v.OcrFinishedAt,
                    OcrInvalidateDigitalSignatures = v.OcrInvalidateDigitalSignatures
                }).ToList(),
                Workflow = wfVm
            };

            vm.SelectedVersionId =
                versionId
                ?? vm.CurrentVersionId
                ?? vm.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault()?.Id;

            ViewBag.OpenClassify = openClassify == true;
            ViewBag.OcrSwitched = ocrSwitched == true;

            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar Details. DocId={DocId}", id);
            TempData["erro"] = "Erro ao carregar detalhes do documento.";
            return RedirectToAction(nameof(Index));
        }
    }

    // =========================
    // WORKFLOW ACTIONS (TRAVA)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartWorkflow([FromForm] Guid documentId, [FromForm] Guid workflowId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var hasCls = await _clsQ.HasClassificationAsync(tenantId, documentId, ct);
            if (!hasCls)
            {
                TempData["Error"] = "Este documento precisa estar classificado antes de iniciar o workflow.";
                return RedirectToAction(nameof(Details), new { id = documentId, openClassify = true });
            }

            var result = await _docWfC.StartAsync(tenantId, documentId, workflowId, userId, ct);
            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha ao iniciar workflow.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            TempData["Success"] = "Workflow iniciado com sucesso.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro StartWorkflow. DocId={DocId} WorkflowId={WorkflowId}", documentId, workflowId);
            TempData["Error"] = "Erro inesperado ao iniciar workflow.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyTransition(
        [FromForm] Guid documentId,
        [FromForm] Guid transitionId,
        [FromForm] string? reason,
        [FromForm] string? comments,
        CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var hasCls = await _clsQ.HasClassificationAsync(tenantId, documentId, ct);
            if (!hasCls)
            {
                TempData["Error"] = "Documento sem classificação. Classifique antes de avançar no workflow.";
                return RedirectToAction(nameof(Details), new { id = documentId, openClassify = true });
            }

            var result = await _docWfC.ApplyTransitionAsync(
                tenantId,
                documentId,
                transitionId,
                reason,
                comments,
                userId,
                ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha ao aplicar transição.";
                return RedirectToAction(nameof(Details), new { id = documentId });
            }

            TempData["Success"] = "Transição aplicada com sucesso.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ApplyTransition. DocId={DocId} TransitionId={TransitionId}", documentId, transitionId);
            TempData["Error"] = "Erro inesperado ao aplicar transição.";
            return RedirectToAction(nameof(Details), new { id = documentId });
        }
    }

    // =========================
    // CRIAR PASTA (modal)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder([FromForm] CreateFolderCommand cmd, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var result = await _folderCommands.CreateAsync(
                tenantId,
                cmd.Name,
                cmd.ParentId,
                cmd.DepartmentId,
                userId,
                ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha ao criar pasta.";
                return RedirectToAction(nameof(Index), new { folderId = cmd.ParentId });
            }

            TempData["Success"] = "Pasta criada com sucesso.";
            return RedirectToAction(nameof(Index), new { folderId = result.Value });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar pasta. ParentId={ParentId} Name={Name}", cmd.ParentId, cmd.Name);
            TempData["Error"] = "Erro ao criar pasta.";
            return RedirectToAction(nameof(Index), new { folderId = cmd.ParentId });
        }
    }

    // =========================
    // UPLOAD
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(
        [FromForm] Guid folderId,
        [FromForm] IFormFile file,
        [FromForm] string? title,
        [FromForm] bool? isConfidential,
        CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            if (file == null || file.Length <= 0)
            {
                TempData["Error"] = "Selecione um arquivo válido.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var ua = Request.Headers.UserAgent.ToString();

            await using var stream = file.OpenReadStream();

            var cmd = new UploadDocumentCommand
            {
                FolderId = folderId,
                Title = string.IsNullOrWhiteSpace(title)
                    ? Path.GetFileNameWithoutExtension(file.FileName)
                    : title.Trim(),
                Description = null,
                IsConfidential = isConfidential ?? false,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Content = stream
            };

            var result = await _documentApp.UploadAsync(cmd, ip, ua, ct);

            if (!result.Success)
            {
                TempData["Error"] = result.Error?.Message ?? "Falha no upload.";
                return RedirectToAction(nameof(Index), new { folderId });
            }

            TempData["Success"] = "Upload realizado com sucesso.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no upload do GED. FolderId={FolderId}", folderId);
            TempData["Error"] = "Erro ao enviar arquivo.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
    }

    // =========================
    // DOWNLOAD (por versionId)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
            {
                TempData["Error"] = "Usuário não autenticado.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, id, ct);
            if (v == null) return NotFound();

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
            var contentType = string.IsNullOrWhiteSpace(v.ContentType) ? "application/octet-stream" : v.ContentType;

            return File(stream, contentType, v.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Download. VersionId={VersionId}", id);
            return StatusCode(500);
        }
    }

    // =========================
    // PREVIEW (inline antigo)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, id, ct);
            if (v == null) return NotFound();

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
            var contentType = string.IsNullOrWhiteSpace(v.ContentType) ? "application/octet-stream" : v.ContentType;

            SetInlineContentDisposition(v.FileName);

            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Preview. VersionId={VersionId}", id);
            return StatusCode(500);
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadVersion(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            var stream = await _storage.OpenReadAsync(v.StoragePath, ct);
            return File(stream, v.ContentType, v.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em DownloadVersion. VersionId={VersionId}", versionId);
            return StatusCode(500);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Folders(CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;
            var tree = await _folders.TreeAsync(tenantId, ct);

            var vm = new FoldersPageVM
            {
                Tree = tree.Select(x => new FoldersPageVM.FolderVM
                {
                    Id = x.Id,
                    ParentId = x.ParentId,
                    Name = x.Name,
                    Level = x.Level
                }).ToList()
            };

            return View(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar tela de pastas.");
            TempData["erro"] = "Erro ao carregar pastas.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarPasta(string nome, Guid? parentId, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var result = await _folderCommands.CreateAsync(
                tenantId: tenantId,
                name: nome,
                parentId: parentId,
                departmentId: null,
                createdBy: userId,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta criada com sucesso." : (result.Error?.Message ?? "Falha ao criar pasta.");

            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar pasta.");
            TempData["erro"] = "Erro ao criar pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenomearPasta(Guid id, string nome, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            var result = await _folderCommands.RenameAsync(
                tenantId: tenantId,
                folderId: id,
                newName: nome,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta renomeada com sucesso." : (result.Error?.Message ?? "Falha ao renomear pasta.");

            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao renomear pasta. FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao renomear pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirPasta(Guid id, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            await _folderCommands.DeactivateAsync(
                tenantId: tenantId,
                folderId: id,
                userId: _currentUser.UserId,
                ct: ct);

            TempData["ok"] = "Pasta excluída com sucesso.";
            return RedirectToAction(nameof(Folders));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desativar pasta. FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao desativar pasta.";
            return RedirectToAction(nameof(Folders));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirPastaExplorer(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("=== DELETE FOLDER START === FolderId={Folder}", id);

        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        try
        {
            var r = await _folderCommands.DeleteRecursiveAsync(tenantId, id, userId, ct);
            if (!r.Success)
            {
                _logger.LogWarning("DELETE FOLDER FAIL: {Msg}", r.Error?.Message);
                TempData["erro"] = r.Error?.Message ?? "Erro ao excluir pasta.";
                return RedirectToAction(nameof(Folders));
            }

            TempData["ok"] = "Pasta excluída.";
            _logger.LogInformation("=== DELETE FOLDER SUCCESS === FolderId={FolderId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== DELETE FOLDER ERROR === FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao excluir pasta.";
        }

        return RedirectToAction(nameof(Folders));
    }

    private static bool IsPdf(string? ct, string name)
        => (!string.IsNullOrWhiteSpace(ct) && ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
           || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsImage(string? ct, string name)
    {
        if (!string.IsNullOrWhiteSpace(ct) && ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    // =========================
    // PREVIEW INLINE (iframe wrapper)
    // =========================
    [HttpGet]
    public async Task<IActionResult> PreviewInline(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (IsImage(v.ContentType, v.FileName))
            {
                var url = Url.Action("PreviewVersion", "Ged", new { versionId })!;
                var html = $@"
<!doctype html>
<html>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<style>
  html,body{{height:100%;margin:0;background:#f5f5f7;}}
  .wrap{{height:100%;display:flex;align-items:center;justify-content:center;overflow:auto;}}
  img{{max-width:100%;max-height:100%;}}
</style>
</head>
<body>
<div class='wrap'><img src='{url}' alt='preview' /></div>
</body>
</html>";
                return Content(html, "text/html", Encoding.UTF8);
            }

            var previewUrl = Url.Action("PreviewVersion", "Ged", new { versionId })!;
            var htmlPdf = $@"
<!doctype html>
<html>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<style>html,body{{height:100%;margin:0;}} iframe{{width:100%;height:100%;border:0;}}</style>
</head>
<body>
<iframe src='{previewUrl}'></iframe>
</body>
</html>";
            return Content(htmlPdf, "text/html", Encoding.UTF8);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em PreviewInline. VersionId={VersionId}", versionId);
            return StatusCode(500);
        }
    }

    // =========================
    // PREVIEW VERSION (inline) - usado no iframe
    // =========================
    [HttpGet]
    public async Task<IActionResult> PreviewVersion(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();

        var tenantId = _currentUser.TenantId;

        try
        {
            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (!await _storage.ExistsAsync(v.StoragePath, ct))
                return NotFound("Arquivo não encontrado no storage.");

            if (IsImage(v.ContentType, v.FileName))
            {
                var img = await _storage.OpenReadAsync(v.StoragePath, ct);
                SetInlineContentDisposition(v.FileName);

                return File(
                    img,
                    string.IsNullOrWhiteSpace(v.ContentType) ? "image/*" : v.ContentType,
                    enableRangeProcessing: true
                );
            }

            if (IsPdf(v.ContentType, v.FileName))
            {
                var pdf = await _storage.OpenReadAsync(v.StoragePath, ct);
                SetInlineContentDisposition(v.FileName);

                return File(pdf, "application/pdf", enableRangeProcessing: true);
            }

            var previewPath = Path.Combine("tenants", tenantId.ToString(), "documents", v.DocumentId.ToString(), "versions", versionId.ToString(), "previews", "preview.pdf");
            if (await _storage.ExistsAsync(previewPath, ct))
            {
                var preview = await _storage.OpenReadAsync(previewPath, ct);

                var previewName = $"{Path.GetFileNameWithoutExtension(v.FileName)}.pdf";
                SetInlineContentDisposition(previewName);

                return File(preview, "application/pdf", enableRangeProcessing: true);
            }

            await _previewStatus.UpsertAsync(tenantId, versionId, PreviewProcessingStatus.Pending, null, null, DateTimeOffset.UtcNow, null, ct);
            await _previewQueue.EnqueueAsync(tenantId, v.DocumentId, versionId, v.StoragePath, v.FileName, ct);

            var retryUrl = Url.Action("PreviewVersion", "Ged", new { versionId })!;
            var html = $@"
<!doctype html>
<html>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1' />
  <title>Gerando visualização…</title>
  <style>
    body{{font-family:system-ui;margin:0;background:#f6f7fb;color:#222}}
    .box{{max-width:720px;margin:10vh auto;padding:24px;background:#fff;border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.08)}}
    .muted{{color:#666}}
    .spinner{{width:24px;height:24px;border:3px solid #ddd;border-top-color:#333;border-radius:50%;animation:spin 1s linear infinite;display:inline-block;vertical-align:middle;margin-right:10px}}
    @keyframes spin{{to{{transform:rotate(360deg)}}}}
    a{{color:#0b5ed7}}
  </style>
</head>
<body>
  <div class='box'>
    <div><span class='spinner'></span><strong>Gerando / atualizando visualização…</strong></div>
    <p class='muted'>Isso pode levar alguns segundos. A página vai tentar novamente automaticamente.</p>
    <p class='muted'>Se demorar muito, clique em <a href='{retryUrl}'>tentar novamente</a>.</p>
  </div>
  <script>
    const statusUrl = '@Url.Action("PreviewStatus", "Ged", new { versionId })';
    let wait = 1200;
    async function tick() {
      const res = await fetch(statusUrl, { headers: { 'Accept':'application/json' }});
      if (res.ok) {
        const data = await res.json();
        if (data.status === 'READY' && data.previewUrl) { location.href = data.previewUrl; return; }
        if (data.status === 'ERROR') { return; }
      }
      setTimeout(tick, wait);
      wait = Math.min(wait * 1.7, 10000);
    }
    setTimeout(tick, wait);
  </script>
</body>
</html>";
            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em PreviewVersion. Tenant={TenantId}, VersionId={VersionId}", tenantId, versionId);

            var retryUrl = Url.Action("PreviewVersion", "Ged", new { versionId })!;
            var htmlErr = $@"
<!doctype html>
<html>
<head>
  <meta charset='utf-8' />
  <meta name='viewport' content='width=device-width, initial-scale=1' />
  <title>Falha na visualização</title>
  <style>
    body{{font-family:system-ui;margin:0;background:#f6f7fb;color:#222}}
    .box{{max-width:720px;margin:10vh auto;padding:24px;background:#fff;border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.08)}}
    .muted{{color:#666}} a{{color:#0b5ed7}}
  </style>
</head>
<body>
  <div class='box'>
    <h3>Falha ao gerar a visualização</h3>
    <p class='muted'>O servidor registrou um erro ao converter o arquivo para PDF. Verifique o log para detalhes.</p>
    <p class='muted'>Tente novamente: <a href='{retryUrl}'>recarregar</a></p>
  </div>
</body>
</html>";
            return Content(htmlErr, "text/html; charset=utf-8");
        }
    }

    [HttpGet]
    public async Task<IActionResult> PreviewStatus(Guid versionId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated) return Unauthorized();
        var tenantId = _currentUser.TenantId;
        var status = await _previewStatus.GetAsync(tenantId, versionId, ct);
        if (status is null)
            return Json(new { status = "PENDING" });

        var previewUrl = status.Status == PreviewProcessingStatus.Ready && !string.IsNullOrWhiteSpace(status.PreviewPath)
            ? $"/storage/{status.PreviewPath}"
            : null;
        return Json(new
        {
            status = status.Status.ToString().ToUpperInvariant(),
            previewUrl,
            errorMessage = status.ErrorMessage,
            requestedAt = status.RequestedAt,
            finishedAt = status.FinishedAt
        });
    }

    // =========================
    // RUN OCR (enfileira)
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunOcr(Guid versionId, bool force = false, CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return Unauthorized();

            var tenantId = _currentUser.TenantId;
            var userId = _currentUser.UserId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null)
                return NotFound();

            var alreadyCompleted = await _ocrJobs.HasCompletedAsync(tenantId, versionId, ct);
            var latestStatus = await _ocrJobs.GetLatestByVersionIdAsync(tenantId, versionId, ct);

            var alreadyRunning = latestStatus is not null &&
                                 (string.Equals(latestStatus.Status.ToString(), "PENDING", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(latestStatus.Status.ToString(), "PROCESSING", StringComparison.OrdinalIgnoreCase));

            if (alreadyRunning)
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        success = true,
                        alreadyRunning = true,
                        message = "OCR já está em processamento para esta versão.",
                        versionId,
                        jobId = latestStatus!.JobId
                    });
                }

                TempData["Success"] = "OCR já está em processamento para esta versão.";
                return RedirectToAction(nameof(Details), new { id = v.DocumentId, versionId });
            }

            if (alreadyCompleted && !force)
            {
                if (IsAjaxRequest())
                {
                    return Json(new
                    {
                        success = true,
                        alreadyCompleted = true,
                        message = "OCR já foi executado para esta versão.",
                        versionId
                    });
                }

                TempData["Success"] = "OCR já foi executado para esta versão.";
                return RedirectToAction(nameof(Details), new { id = v.DocumentId, versionId });
            }

            var jobId = await _ocrJobs.EnqueueAsync(
                tenantId,
                versionId,
                userId,
                invalidateDigitalSignatures: force,
                ct);

            await _ocrNotifier.PublishOcrStatusAsync(tenantId, versionId, "PENDING", jobId, "OCR enfileirado", ct);

            if (IsAjaxRequest())
            {
                return Json(new
                {
                    success = true,
                    message = "OCR solicitado. O processamento será executado em segundo plano.",
                    versionId,
                    jobId
                });
            }

            TempData["Success"] = "OCR solicitado. Acompanhe o status na lista de versões.";

            return RedirectToAction(nameof(Details), new
            {
                id = v.DocumentId,
                versionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao solicitar OCR. VersionId={VersionId}", versionId);

            if (IsAjaxRequest())
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = "Erro ao solicitar OCR."
                });
            }

            TempData["Error"] = "Erro ao solicitar OCR.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReprocessOcr(Guid versionId, CancellationToken ct = default)
    {
        return await RunOcr(versionId, force: true, ct);
    }

    [HttpGet]
    public async Task<IActionResult> OcrStatus(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();

            var tenantId = _currentUser.TenantId;

            var status = await _ocrJobs.GetLatestByVersionIdAsync(tenantId, versionId, ct);

            if (status is null)
            {
                return Json(new
                {
                    success = true,
                    hasJob = false,
                    status = "NONE",
                    label = "Não executado",
                    completed = false,
                    error = false
                });
            }

            var st = status.Status.ToString().ToUpperInvariant();

            return Json(new
            {
                success = true,
                hasJob = true,
                versionId,
                jobId = status.JobId,
                status = st,
                label = st switch
                {
                    "PENDING" => "Pendente",
                    "PROCESSING" => "Executando",
                    "COMPLETED" => "Concluído",
                    "ERROR" => "Erro",
                    _ => st
                },
                requestedAt = status.RequestedAt,
                startedAt = status.StartedAt,
                finishedAt = status.FinishedAt,
                errorMessage = status.ErrorMessage,
                completed = st == "COMPLETED",
                error = st == "ERROR"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar status OCR. VersionId={VersionId}", versionId);

            return StatusCode(500, new
            {
                success = false,
                message = "Erro ao consultar status OCR."
            });
        }
    }

    private async Task InsertOcrRequestAuditAsync(
        Guid tenantId,
        Guid documentId,
        Guid? userId,
        long jobId,
        Guid versionId,
        bool force,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO ged.document_classification_audit
(
  id, tenant_id, document_id, user_id,
  action, method,
  before_json, after_json,
  source, created_at, reg_status
)
VALUES
(
  gen_random_uuid(), @tenantId, @documentId, @userId,
  'OCR_REQUESTED', 'OCR',
  '{}'::jsonb,
  jsonb_build_object(
      'jobId', @jobId,
      'versionId', @versionId,
      'force', @force
  ),
  'WEB', now(), 'A'
);";

        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    tenantId,
                    documentId,
                    userId,
                    jobId,
                    versionId,
                    force
                },
                cancellationToken: ct));
    }

    [HttpGet]
    public async Task<IActionResult> OcrText(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            var ocrRelPath = Path.Combine(
                tenantId.ToString("N"),
                "ocr",
                v.DocumentId.ToString("N"),
                versionId.ToString("N"),
                "ocr.txt"
            ).Replace('\\', '/');

            if (!await _storage.ExistsAsync(ocrRelPath, ct))
                return NotFound("OCR ainda não gerado.");

            var stream = await _storage.OpenReadAsync(ocrRelPath, ct);
            return File(stream, "text/plain; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em OcrText. VersionId={VersionId}", versionId);
            return StatusCode(500);
        }
    }

    // =========================
    // SEARCH (FULL-TEXT)
    // =========================
    [HttpGet]
    public async Task<IActionResult> Search(string? q, Guid? folderId, int limit = 25, CancellationToken ct = default)
    {
        try
        {
            if (!_currentUser.IsAuthenticated)
                return RedirectToAction("Login", "Account");

            var tenantId = _currentUser.TenantId;

            var rows = await _search.SearchAsync(
                tenantId: tenantId,
                q: q ?? "",
                folderId: folderId,
                limit: limit,
                ct: ct);

            ViewBag.Query = q ?? "";
            ViewBag.FolderId = folderId;

            return View("Search", rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em Search. FolderId={FolderId} q={q}", folderId, q);
            TempData["erro"] = "Erro ao pesquisar.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
    }

    

    // =========================
    // REGENERATE PREVIEW
    // =========================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegeneratePreview(Guid versionId, CancellationToken ct)
    {
        try
        {
            if (!_currentUser.IsAuthenticated) return Unauthorized();
            var tenantId = _currentUser.TenantId;

            var v = await _docs.GetVersionForDownloadAsync(tenantId, versionId, ct);
            if (v is null) return NotFound();

            if (IsImage(v.ContentType, v.FileName) || IsPdf(v.ContentType, v.FileName))
                return RedirectToAction(nameof(Details), new { id = v.DocumentId });

            var previewRelPath = Path.Combine(
                tenantId.ToString("N"),
                "previews",
                v.DocumentId.ToString("N"),
                versionId.ToString("N"),
                "preview.pdf"
            ).Replace('\\', '/');

            await _storage.DeleteIfExistsAsync(previewRelPath, ct);

            _ = await _preview.GetOrCreatePreviewPdfAsync(tenantId, v.DocumentId, versionId, v.StoragePath, v.FileName, ct);

            TempData["ok"] = "Preview regerado com sucesso.";
            return RedirectToAction(nameof(Details), new { id = v.DocumentId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro em RegeneratePreview. VersionId={VersionId}", versionId);
            TempData["erro"] = "Erro ao regerar preview.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenomearPastaExplorer(Guid id, string nome, Guid? folderId, CancellationToken ct)
    {
        try
        {
            var tenantId = _currentUser.TenantId;

            var result = await _folderCommands.RenameAsync(
                tenantId: tenantId,
                folderId: id,
                newName: nome,
                ct: ct);

            TempData[result.Success ? "ok" : "erro"] =
                result.Success ? "Pasta renomeada com sucesso." : (result.Error?.Message ?? "Falha ao renomear pasta.");

            return RedirectToAction(nameof(Index), new { folderId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao renomear pasta (Explorer). FolderId={FolderId}", id);
            TempData["erro"] = "Erro ao renomear pasta.";
            return RedirectToAction(nameof(Index), new { folderId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid? folderId, CancellationToken ct)
    {
        _logger.LogInformation("=== DELETE DOCUMENT START ===");
        _logger.LogInformation("Request DeleteDocument | DocId={DocId} FolderId={FolderId}", id, folderId);

        if (!_currentUser.IsAuthenticated)
        {
            _logger.LogWarning("DeleteDocument: usuário NÃO autenticado");
            return Unauthorized();
        }

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;

        _logger.LogInformation("DeleteDocument Context | Tenant={TenantId} User={UserId}", tenantId, userId);

        Result result;
        try
        {
            _logger.LogInformation("Chamando _documentCommands.DeleteAsync...");
            result = await _documentCommands.DeleteAsync(tenantId, id, userId, ct);
            _logger.LogInformation("Retorno DeleteAsync | Success={Success} Error={Error}", result.Success, result.Error?.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXCEPTION em DeleteDocument controller. DocId={DocId}", id);
            TempData["erro"] = "Erro inesperado ao excluir documento.";
            return RedirectToAction(nameof(Index), new { folderId });
        }

        TempData[result.Success ? "ok" : "erro"] =
            result.Success ? "Documento excluído com sucesso." : (result.Error?.Message ?? "Falha ao excluir.");

        _logger.LogInformation("=== DELETE DOCUMENT END ===");

        return RedirectToAction(nameof(Index), new { folderId });
    }

  
    // =========================
    // Content-Disposition seguro
    // =========================
    private void SetInlineContentDisposition(string fileName)
    {
        fileName = SanitizeFileName(fileName);

        var asciiFallback = ToAsciiFileName(fileName);
        if (string.IsNullOrWhiteSpace(asciiFallback))
            asciiFallback = "preview";

        var cd = new ContentDispositionHeaderValue("inline")
        {
            FileName = QuoteIfNeeded(asciiFallback),
            FileNameStar = fileName
        };

        Response.Headers[HeaderNames.ContentDisposition] = cd.ToString();
    }

    private static string SanitizeFileName(string fileName)
    {
        fileName = (fileName ?? "").Trim()
            .Replace("\r", "")
            .Replace("\n", "")
            .Replace("\"", "'");

        if (fileName.Length > 180)
            fileName = fileName[..180];

        return fileName;
    }

    private static string ToAsciiFileName(string fileName)
    {
        var normalized = fileName.Normalize(NormalizationForm.FormD);

        Span<char> buffer = stackalloc char[normalized.Length];
        var idx = 0;

        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc == UnicodeCategory.NonSpacingMark) continue;

            if (ch >= 32 && ch <= 126)
                buffer[idx++] = ch;
        }

        var ascii = new string(buffer[..idx]);

        foreach (var bad in Path.GetInvalidFileNameChars())
            ascii = ascii.Replace(bad.ToString(), "");

        return ascii.Replace(";", "_").Replace(",", "_").Trim();
    }

    private static string QuoteIfNeeded(string value)
        => value.Contains(' ') ? $"\"{value}\"" : value;
}
