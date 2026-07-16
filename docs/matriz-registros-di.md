# Matriz de registros de DI

Data: 2026-07-14

| contrato | implementação | lifetime | consumidor | projeto de registro | status | observação |
|---|---|---:|---|---|---|---|
| `ICurrentUser` | `WebGed.WebApi.Security.CurrentUser` / `InovaGed.Web.Common.Context.CurrentUser` | Scoped | Controllers e serviços por request | Host WebApi/MVC | OK | Específico da camada de apresentação porque depende de `HttpContext`. |
| `IDbConnectionFactory` | `NpgsqlConnectionFactory` | Singleton | Repositories, auditoria, Guardião, movimentação | `InovaGed.Infrastructure` | OK | Factory stateless configurada pela connection string. |
| `DocumentAppService` | `DocumentAppService` | Scoped | `DocumentsController`, OCR worker | `InovaGed.Infrastructure` via `AddInovaGedApplication` | OK | Application service por request; depende de usuário, storage, preview, OCR, auditoria e repository. |
| `IPreviewGenerator` | `LibreOfficePreviewGenerator` | Scoped | `DocumentAppService`, workers de preview/OCR | `InovaGed.Infrastructure` | OK | Depende de storage, options e logger. |
| `IPdfTextExtractor` | `PopplerPdfTextExtractor` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Serviço leve sem estado, mantido scoped por consistência com pipeline de request. |
| `IDocumentWriteRepository` | `DocumentWriteRepository` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Usa conexão/transação por operação. |
| `IAuditLogWriter` | `AuditLogWriter` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Auditoria transacional por request/operação. |
| `IFileStorage` | `LocalFileStorage` | Scoped | `DocumentAppService`, preview | `InovaGed.Infrastructure` | OK | Usa options e I/O local. |
| `IAuditWriter` | `AuditWriter` | Scoped | `DocumentMoveService`, `DocumentGuardianService` | `InovaGed.Infrastructure` | OK | Auditoria por request/operação. |
| `IDocumentMoveService` | `DocumentMoveService` | Scoped | `DocumentsController` | `InovaGed.Infrastructure` | OK | Depende de banco, auditoria e permissão. |
| `IPermissionService` | `PermissionService` | Scoped | `DocumentMoveService` | `InovaGed.Infrastructure` | OK | Usa banco e cache. |
| `IPermissionChecker` | `AllowAllPermissionChecker` | Scoped | `PermissionService`/serviços de permissão legados | `InovaGed.Infrastructure` | OK | Mantém compatibilidade com composição MVC existente. |
| `IDocumentGuardianService` | `DocumentGuardianService` | Scoped | API/Controller do Guardião | `InovaGed.Infrastructure` | OK | Registrado para composição e evolução da API do Guardião. |
| `LocalStorageOptions` | Options `Storage:Local` | Singleton/options | `LocalFileStorage` | `InovaGed.Infrastructure` | OK | Validado no startup. |
| `LibreOfficeOptions` | Options `Preview` | Singleton/options | `LibreOfficePreviewGenerator` | `InovaGed.Infrastructure` | OK | Ligado e validado no startup. |
| `SuspiciousRequestOptions` | Options `SuspiciousRequest` | Singleton/options | `SuspiciousRequestMiddleware` | `InovaGed.Web` | OK | Limites de path e campos de log configuráveis. |
