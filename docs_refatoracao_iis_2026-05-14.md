# InovaGED — Plano de refatoração (baseado em comportamento observado no IIS em 14/05/2026)

## 1) Endpoints críticos e gargalos

| Endpoint | Sintoma observado | Causa provável | Prioridade |
|---|---|---|---|
| `POST /Ged/Upload` | 5s–78s, bloqueio da UI | Upload + OCR síncronos no request thread | **Crítico** |
| `GET /Ged/PreviewVersion` / `GET /Ged/PreviewInline` | Polling repetido, 500ms–6s | Pré-visualização sendo recalculada + polling agressivo | **Crítico** |
| `GET /Ged/OcrStatus` | Polling intenso (180ms–500ms) | Front consultando status continuamente | **Crítico** |
| `GET /ClassificationDashboard/Count` | Chamadas frequentes após ações (180ms–3s) | Requisições redundantes e sem cache | Médio |
| `GET /Ged/Details` | Render sequencial (200ms–3s) | N+1, consultas derivadas e payload excessivo | Médio |

---

## 2) Mapeamento de módulos front-end (híbrido Razor + React parcial)

| Módulo | Estado atual | Problema | Solução recomendada |
|---|---|---|---|
| Detalhes GED (Razor) | Razor com scripts imperativos | Muitos listeners + atualizações parciais sem controle de estado | Manter Razor shell, mover blocos dinâmicos (status OCR/preview) para ilhas React |
| Preview OCR | Polling periódico (`/Ged/PreviewVersion`) | Sobrecarga no servidor e jitter de UI | Trocar por SignalR + fallback polling exponencial |
| Fila OCR (`ged-queue-visualizer.js`) | Script pesado no carregamento inicial | Custo de parse/exec alto em páginas não relacionadas | Lazy-load com `import()` sob demanda |
| Status OCR (`ged-ocr-status.js`) | Polling curto e contínuo | Requests redundantes + disputas de render | Estado reativo em React + push em tempo real |
| Dashboard de classificação | Requisições frequentes ao contador | Falta de cache e invalidação inteligente | Endpoint agregado + cache de 10–30s + invalidação por evento |

---

## 3) Refatoração backend (fila assíncrona + SignalR)

## 3.1 Upload assíncrono com retorno imediato

**Arquivo alvo:** `InovaGed.Web/Controller/GedController.cs` (método `Upload`)

### Contratos novos

```csharp
public interface IUploadQueueService
{
    Task<Guid> EnqueueAsync(UploadEnvelope envelope, CancellationToken ct);
}

public sealed record UploadEnvelope(
    Guid TenantId,
    Guid DocumentId,
    Guid VersionId,
    string FilePath,
    string ContentType,
    string UploadedBy,
    DateTimeOffset RequestedAt
);
```

### Controller (substituir fluxo síncrono)

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Upload(IFormFile file, Guid documentId, CancellationToken ct)
{
    if (file is null || file.Length == 0)
        return BadRequest(new { ok = false, message = "Arquivo inválido." });

    var tenantId = _currentUser.TenantId;
    var versionId = Guid.NewGuid();

    var tempFile = await _fileStorage.SaveTempAsync(file, tenantId, ct);

    var jobId = await _uploadQueue.EnqueueAsync(new UploadEnvelope(
        tenantId,
        documentId,
        versionId,
        tempFile,
        file.ContentType ?? "application/octet-stream",
        _currentUser.Username,
        DateTimeOffset.UtcNow), ct);

    return Accepted(new
    {
        ok = true,
        versionId,
        jobId,
        status = "queued",
        detailsUrl = Url.Action("Details", "Ged", new { id = documentId, versionId })
    });
}
```

## 3.2 Worker (Hangfire) para upload + OCR

**Arquivo novo sugerido:** `InovaGed.Application/Jobs/ProcessUploadAndOcrJob.cs`

```csharp
public sealed class ProcessUploadAndOcrJob
{
    private readonly IGedWriteService _gedWrite;
    private readonly IOcrPipeline _ocr;
    private readonly IHubContext<GedProcessingHub, IGedProcessingClient> _hub;

    public ProcessUploadAndOcrJob(
        IGedWriteService gedWrite,
        IOcrPipeline ocr,
        IHubContext<GedProcessingHub, IGedProcessingClient> hub)
    {
        _gedWrite = gedWrite;
        _ocr = ocr;
        _hub = hub;
    }

    public async Task ExecuteAsync(UploadEnvelope envelope, CancellationToken ct)
    {
        await _hub.Clients.Group(envelope.TenantId.ToString())
            .UploadStatusChanged(envelope.VersionId, "processing");

        await _gedWrite.AttachFileVersionAsync(envelope, ct);
        await _ocr.RunAsync(envelope.VersionId, ct);

        await _hub.Clients.Group(envelope.TenantId.ToString())
            .UploadStatusChanged(envelope.VersionId, "completed");
    }
}
```

### Registro (Program.cs)

```csharp
builder.Services.AddSignalR();
builder.Services.AddHangfire(cfg => cfg.UseSimpleAssemblyNameTypeSerializer()
                                  .UseRecommendedSerializerSettings()
                                  .UsePostgreSqlStorage(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHangfireServer();
```

---

## 4) SignalR para substituir polling

**Arquivo novo sugerido:** `InovaGed.Web/Hubs/GedProcessingHub.cs`

```csharp
public interface IGedProcessingClient
{
    Task UploadStatusChanged(Guid versionId, string status);
    Task OcrStatusChanged(Guid versionId, string status, int progress);
    Task PreviewReady(Guid versionId, string previewUrl);
}

public sealed class GedProcessingHub : Hub<IGedProcessingClient>
{
    public async Task SubscribeTenant(Guid tenantId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, tenantId.ToString());

    public async Task SubscribeDocument(Guid documentId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"doc:{documentId}");
}
```

**Mapeamento:**

```csharp
app.MapHub<GedProcessingHub>("/hubs/ged-processing");
```

---

## 5) Refatoração front-end (Razor + ilhas React)

## 5.1 React parcial para preview/status

**Arquivo novo sugerido:** `InovaGed.Web/wwwroot/react/ged/GedRealtimePanel.jsx`

```jsx
import React, { useEffect, useState } from "react";
import * as signalR from "@microsoft/signalr";

export function GedRealtimePanel({ tenantId, versionId, previewUrlFallback }) {
  const [ocrStatus, setOcrStatus] = useState("queued");
  const [previewUrl, setPreviewUrl] = useState(null);

  useEffect(() => {
    const conn = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/ged-processing")
      .withAutomaticReconnect()
      .build();

    conn.on("OcrStatusChanged", (vId, status) => {
      if (vId === versionId) setOcrStatus(status);
    });

    conn.on("PreviewReady", (vId, url) => {
      if (vId === versionId) setPreviewUrl(url);
    });

    conn.start()
      .then(() => conn.invoke("SubscribeTenant", tenantId))
      .catch(() => {
        // fallback degradado: polling exponencial curto
      });

    return () => conn.stop();
  }, [tenantId, versionId]);

  return (
    <section>
      <span className={`badge ${ocrStatus === "completed" ? "bg-success" : "bg-warning"}`}>
        OCR: {ocrStatus}
      </span>
      <iframe src={previewUrl ?? previewUrlFallback} title="Preview GED" />
    </section>
  );
}
```

## 5.2 Lazy loading de scripts pesados

**Arquivo alvo:** `InovaGed.Web/Views/Ged/Details.cshtml`

```html
<script type="module">
  const shouldLoadQueue = document.querySelector('[data-queue-visualizer]');
  if (shouldLoadQueue) {
    import('/js/ged-queue-visualizer.js');
  }

  const shouldLoadOcr = document.querySelector('[data-ocr-live]');
  if (shouldLoadOcr) {
    import('/js/ged-ocr-status.js');
  }
</script>
```

---

## 6) Otimização de banco (PostgreSQL + Dapper)

## 6.1 Índices para consultas quentes

**Arquivo novo sugerido:** `InovaGed.Infrastructure/Migrations/20260514_AddGedHotPathIndexes.sql`

```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_doc_versions_document_created
ON ged.document_versions (document_id, created_at DESC);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_doc_versions_ocr_status
ON ged.document_versions (ocr_status);

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_class_queue_sector_status
ON ged.classification_queue (sector_id, status, updated_at DESC);
```

## 6.2 View materializada para dashboard

```sql
CREATE MATERIALIZED VIEW IF NOT EXISTS ged.mv_classification_dashboard AS
SELECT
  sector_id,
  status,
  COUNT(*) AS total,
  MAX(updated_at) AS last_update
FROM ged.classification_queue
GROUP BY sector_id, status;

CREATE UNIQUE INDEX IF NOT EXISTS ix_mv_class_dashboard_sector_status
ON ged.mv_classification_dashboard (sector_id, status);
```

**Refresh assíncrono pós-evento:**

```sql
REFRESH MATERIALIZED VIEW CONCURRENTLY ged.mv_classification_dashboard;
```

---

## 7) Caching e redução de chamadas redundantes

**Arquivo alvo:** `InovaGed.Web/Controller/ClassificationDashboardController.cs` (método `Count`)

```csharp
[HttpGet("/ClassificationDashboard/Count")]
public async Task<IActionResult> Count(CancellationToken ct)
{
    var tenantId = _currentUser.TenantId;
    var key = $"classdash:count:{tenantId}";

    var value = await _cache.GetOrCreateAsync(key, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15);
        return await _dash.GetCountAsync(tenantId, ct);
    });

    return Json(new { count = value });
}
```

---

## 8) Checklist detalhado (prioridades)

### Crítico (Semana 1)
- [ ] Quebrar `POST /Ged/Upload` em **enqueue + worker**.
- [ ] Publicar eventos de OCR/preview por SignalR.
- [ ] Substituir polling de `/Ged/OcrStatus` e `/Ged/PreviewVersion` por push (com fallback).
- [ ] Medir p95/p99 de upload e tempo até preview disponível.

### Médio (Semana 2)
- [ ] Cachear `/ClassificationDashboard/Count` (10–30s).
- [ ] Consolidar consultas de `GET /Ged/Details` para evitar N+1.
- [ ] Criar índices de hot path e MV de dashboard.

### Baixo (Semana 3)
- [ ] Lazy-load de `ged-queue-visualizer.js` e `ged-ocr-status.js`.
- [ ] Reduzir manipulação manual de DOM, migrando widgets para React controlado.
- [ ] Limpar CSS/JS inline remanescentes.

---

## 9) Ganhos esperados

- **Upload percebido:** de bloqueante para resposta imediata (`202 Accepted`) com progresso em tempo real.
- **Carga HTTP:** redução drástica de GETs de polling em horários de pico.
- **UX:** menos flicker e estados inconsistentes no preview/OCR.
- **Banco:** menor variação de latência para contadores e dashboards com cache + MV.
