# Relatório de consolidação de DI

## Causa da ambiguidade

O erro `CS0121` ocorria porque existiam dois métodos de extensão públicos com o mesmo nome e a mesma assinatura lógica para `IServiceCollection` e `IConfiguration`:

- `InovaGed.Application.DependencyInjection.AddInovaGedApplication(...)`;
- `InovaGed.Infrastructure.DependencyInjection.AddInovaGedApplication(...)`.

Quando `Program.cs` importava simultaneamente os namespaces `InovaGed.Application` e `InovaGed.Infrastructure`, a chamada fluente `builder.Services.AddInovaGedApplication(builder.Configuration)` ficava ambígua.

## Métodos encontrados

A busca inicial localizou:

- `InovaGed.Application/DependencyInjection.cs`: `AddInovaGedApplication`, responsável por serviços puros da camada Application;
- `InovaGed.Infrastructure/DependencyInjection.cs`: um `AddInovaGedApplication` incorreto, duplicado, e `AddInovaGedInfrastructure`;
- `InovaGed.Web/Program.cs`: composição do MVC;
- `WebGed.WebApi/Program.cs`: composição da API.

## Método renomeado e classes ajustadas

- A classe estática da Application foi renomeada de `DependencyInjection` para `ApplicationServiceCollectionExtensions`.
- A classe estática da Infrastructure foi renomeada de `DependencyInjection` para `InfrastructureServiceCollectionExtensions`.
- O método incorreto `AddInovaGedApplication` da Infrastructure foi removido, sem alias temporário.
- A Infrastructure mantém apenas `AddInovaGedInfrastructure` para seus registros.

## Registros por camada

### Application

`AddInovaGedApplication` registra apenas serviços de aplicação e sugestão/classificação puros:

- `DocumentAppService`;
- `DocumentClassificationAppService`;
- `SimpleTextDocumentTypeSuggester`;
- `HybridDocumentTypeSuggester`.

### Infrastructure

`AddInovaGedInfrastructure` passou a orquestrar módulos explícitos:

- `AddDatabaseModule`;
- `AddGedModule`;
- `AddOcrModule`;
- `AddPreviewModule`;
- `AddGuardianModule`;
- `AddSecurityOperationsModule`;
- `AddInfrastructureHealthModule`.

Os registros de Infrastructure incluem factory de conexão, storage, repositories de escrita, preview, OCR, auditoria, permissões, Guardião e health check central.

## Duplicidades removidas

Foram removidos do host MVC registros que agora pertencem à composição central de Infrastructure, incluindo:

- `IDbConnectionFactory`;
- `IFileStorage`;
- `IPreviewGenerator`;
- `IPdfTextExtractor`;
- `IDocumentWriteRepository`;
- `IAuditLogWriter`;
- `IAuditWriter`;
- `IPermissionChecker`;
- `PermissionService` / `IPermissionService`;
- `IDocumentMoveService`;
- `IDocumentGuardianService`;
- `AddMemoryCache` duplicado.

No WebApi foi removido `AddMemoryCache` manual, pois a composição compartilhada já o registra.

## Opções configuradas e validadas

A Infrastructure centraliza opções dos módulos:

- `LocalStorageOptions` em `Storage:Local`, com validação de `RootPath` obrigatório;
- `StorageLocalOptions` em `Storage:Local`;
- `OcrOptions` em `Ocr`, com `ValidateDataAnnotations` e `ValidateOnStart`;
- `LibreOfficeOptions` em `Preview`, com `ValidateDataAnnotations` e `ValidateOnStart`.

Opções específicas de host, UI, autenticação, upload e workers condicionais continuam no respectivo `Program.cs` quando dependem do host.

## Lifetimes revisados

- `IDbConnectionFactory`: singleton, por ser factory stateless/thread-safe com connection string imutável.
- Repositories, serviços de aplicação, auditoria por request, permissões, storage, OCR, preview e Guardião: scoped.
- Catálogo interno de módulos e descritores de módulo: singleton, pois são metadados de composição imutáveis.
- Hosted services permanecem registrados nos hosts onde são efetivamente executados, evitando duplicidade no método compartilhado.

## Catálogo interno de módulos

Foi criado um catálogo interno (`IInfrastructureModuleCatalog`) com descritores contendo:

- módulo;
- habilitado;
- dependências;
- configuração válida;
- health;
- versão;
- última falha.

Esse catálogo é registrado como singleton para consumo interno por telas/serviços de System Health, sem exposição automática para usuários comuns.

## Health checks

A composição central mantém o health check `inovaged-dependencies`. Os módulos declaram status inicial no catálogo, permitindo evolução para checks específicos por banco, storage, OCR, LibreOffice, Tesseract, PACS, filas, Guardião, workers e notificações.

## Testes adicionados/evoluídos

Os testes de composição cobrem:

- build validado da composição Application + Infrastructure;
- resolução de serviços críticos (`DocumentAppService`, `IDocumentMoveService`, `IDocumentGuardianService`);
- build validado da composição WebApi;
- resolução de controllers da WebApi;
- regressão arquitetural contra métodos DI ambíguos;
- existência do catálogo interno de módulos.

## Build e testes

Neste ambiente, o SDK `dotnet` não está instalado (`dotnet: command not found`). Por isso, os comandos de clean, restore, build e test não puderam ser executados localmente. A validação textual com `rg` confirmou a remoção do método ambíguo da Infrastructure e a presença de um único método central de Infrastructure.

## Próximo ciclo recomendado

1. Executar a pipeline com SDK .NET disponível.
2. Expandir os módulos para absorver gradualmente os demais registros hoje específicos do MVC que não dependem de UI.
3. Implementar health checks concretos por módulo.
4. Conectar o catálogo interno às telas administrativas de System Health com autorização restrita.
5. Evoluir Central de Pendências, Meu Trabalho, Guardião e Dossiês sobre a composição modular já separada.
