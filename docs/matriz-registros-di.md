# Matriz de registros de DI — WebApi

| contrato | implementação | lifetime | consumidor | projeto de registro | status | observação |
|---|---|---:|---|---|---|---|
| `ICurrentUser` | `WebGed.WebApi.Security.CurrentUser` | Scoped | `DocumentsController`, `DocumentAppService` | `WebGed.WebApi` | OK | Depende de `IHttpContextAccessor`; por request. |
| `IDbConnectionFactory` | `NpgsqlConnectionFactory` | Singleton | repositories e serviços GED | `InovaGed.Infrastructure` | OK | Factory stateless criada a partir de connection string. |
| `IAuditWriter` | `AuditWriter` | Scoped | `DocumentMoveService`, Guardião | `InovaGed.Infrastructure` | OK | Escreve auditoria por operação/request. |
| `IAuditLogWriter` | `AuditLogWriter` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Dependência transitiva do upload. |
| `DocumentAppService` | `DocumentAppService` | Scoped | `DocumentsController`, workers | `InovaGed.Application` | OK | Application service por request. |
| `IDocumentMoveService` | `DocumentMoveService` | Scoped | `DocumentsController` | `InovaGed.Infrastructure` | OK | Corrigido grafo transitivo. |
| `IPermissionService` | `PermissionService` | Scoped | `DocumentMoveService` | `InovaGed.Infrastructure` | OK | Tipo ausente transitivo identificado. |
| `IDocumentWriteRepository` | `DocumentWriteRepository` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Repository por operação. |
| `IFileStorage` | `LocalFileStorage` | Scoped | `DocumentAppService`, preview/OCR | `InovaGed.Infrastructure` | OK | Usa opções de storage local. |
| `IPreviewGenerator` | `LibreOfficePreviewGenerator` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Usa storage e opções de preview. |
| `IPdfTextExtractor` | `PopplerPdfTextExtractor` | Scoped | `DocumentAppService` | `InovaGed.Infrastructure` | OK | Usa storage/configuração. |
| `IDocumentGuardianService` | `DocumentGuardianService` | Scoped | diagnóstico/testes de DI | `InovaGed.Infrastructure` | OK | Registrado para consolidar módulo Guardião. |
