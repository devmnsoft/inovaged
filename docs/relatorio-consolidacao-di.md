# Relatório de consolidação de DI

## Causa da ambiguidade

A ambiguidade CS0121 ocorria porque havia dois métodos de extensão públicos com a mesma assinatura `AddInovaGedApplication(IServiceCollection, IConfiguration)`: um em `InovaGed.Application` e outro em `InovaGed.Infrastructure`. Quando os hosts importavam os dois namespaces, a chamada fluente não tinha um destino único.

## Métodos encontrados e método renomeado

- `InovaGed.Application.AddInovaGedApplication`: mantido como composição exclusiva da camada Application.
- `InovaGed.Infrastructure.AddInovaGedApplication`: removido como wrapper incorreto.
- `InovaGed.Infrastructure.AddInovaGedInfrastructure`: mantido como composição exclusiva da camada Infrastructure.

As classes estáticas genéricas `DependencyInjection` foram renomeadas para `ApplicationServiceCollectionExtensions` e `InfrastructureServiceCollectionExtensions`.

## Registros da Application

A Application registra somente serviços puros de aplicação e classificação:

- `DocumentAppService` como scoped;
- `DocumentClassificationAppService` como scoped;
- `SimpleTextDocumentTypeSuggester` como scoped;
- `HybridDocumentTypeSuggester` como scoped.

## Registros da Infrastructure e módulos criados

`AddInovaGedInfrastructure` passou a orquestrar módulos explícitos:

- `AddDatabaseModule`: `IDbConnectionFactory` e cache compartilhado;
- `AddGedModule`: storage, repositórios e serviços GED, upload e workers GED;
- `AddOcrModule`: OCR, filas, repositórios, dashboard e workers OCR;
- `AddPreviewModule`: preview, LibreOffice, fila e worker de preview;
- `AddClassificationModule`: classificação, queries, comandos e plano de classificação;
- `AddRetentionModule`: temporalidade, filas, casos, termos e worker diário;
- `AddLoansModule`: empréstimos, protocolos e worker de atraso por feature flag;
- `AddGuardianModule`: Guardião e qualidade documental;
- `AddWorkflowModule`: workflow;
- `AddNotificationsModule`: ponto modular reservado para integrações compartilhadas;
- `AddSecurityOperationsModule`: auditoria, permissões, usuários, segurança e operações.

Também foi criado um catálogo interno `IModuleCatalog`/`ModuleCatalog` com nome do módulo, habilitação, dependências, validade de configuração, health, versão e última falha.

## Duplicidades removidas

- Removido o wrapper `AddInovaGedApplication` da Infrastructure.
- Removidos registros manuais duplicados do host MVC para serviços já centralizados na Infrastructure, incluindo `IDbConnectionFactory`, storage, preview, OCR, classificação, GED, auditoria, permissões, Guardião, temporalidade, empréstimos e workers.
- Preservados registros específicos do host MVC/API, como `ICurrentUser`, `ICurrentContext`, autenticação, autorização, MVC, Swagger, SignalR e notificações web.

## Lifetimes revisados

- Scoped: application services, repositories, auditoria por request, `IDocumentMoveService`, `IDocumentGuardianService`, OCR, preview generators e serviços de domínio da Infrastructure.
- Singleton: `IDbConnectionFactory`, clock, timezone service, filas thread-safe, limiter de upload, catálogo de módulos.
- Hosted services: workers passam a ser registrados pelos módulos da Infrastructure e os duplicados foram removidos do MVC.

## Configurações e health checks

- `Storage:Local` é validado com `ValidateOnStart` e exige `RootPath`.
- `Preview`/LibreOffice é vinculado com `ValidateOnStart`.
- Opções existentes de OCR, upload, Guardião/qualidade documental e empréstimos permanecem configuradas nos módulos correspondentes.
- Health check `inovaged-dependencies` permanece registrado pela Infrastructure.

## Testes e CI

Foram adicionados testes de arquitetura/composição para validar ausência de método ambíguo, resolução de serviços críticos e ausência de hosted services duplicados. Os workflows de CI executam restore, build e test em Release e adicionam guarda textual contra reintrodução de classes `DependencyInjection` e excesso de chamadas/definições `AddInovaGedApplication`.

## Build/test local

O ambiente de execução atual não possui o SDK `dotnet` instalado (`dotnet: command not found`), então os comandos de clean/restore/build/test foram registrados como bloqueados por limitação de ambiente e devem ser executados no CI ou em máquina com .NET 8 SDK.
