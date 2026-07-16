# Diagnóstico — serviço ausente no container de DI

## Ambiente

- Repositório: `devmnsoft/inovaged`
- Host MVC: `InovaGed.Web`
- Host API: `WebGed.WebApi`
- Ambiente local do agente: container Linux sem SDK .NET disponível (`dotnet: command not found`)
- CorrelationId de startup registrado em `WebGed.WebApi/Program.cs` por `startupCorrelationId`

## Erro completo capturado

Não foi possível executar a aplicação neste container porque o SDK/Runtime `dotnet` não está instalado. A validação estática encontrou a divergência de composição que explica o erro reportado:

```text
Unable to resolve service for type 'InovaGed.Application.Ged.Documents.IDocumentMoveService'
while attempting to activate 'WebGed.WebApi.Controllers.DocumentsController'.
```

## Interface ausente

- `InovaGed.Application.Ged.Documents.IDocumentMoveService`

## Implementação esperada

- `InovaGed.Infrastructure.Ged.Documents.DocumentMoveService`

## Consumidor

- `WebGed.WebApi.Controllers.DocumentsController`

## Endpoint acessado

- `WebGed.WebApi` expõe endpoints em `DocumentsController`; a falha ocorre na ativação do controller antes da ação quando a API precisa resolver `DocumentAppService`, `IDocumentMoveService`, `ICurrentUser` e `ILogger<DocumentsController>`.

## Stack trace esperada

```text
System.InvalidOperationException: Unable to resolve service for type 'InovaGed.Application.Ged.Documents.IDocumentMoveService' while attempting to activate 'WebGed.WebApi.Controllers.DocumentsController'.
   at Microsoft.Extensions.DependencyInjection.ActivatorUtilities.ThrowHelperUnableToResolveService(Type type, Type requiredBy)
   at lambda_method(...)
   at Microsoft.AspNetCore.Mvc.Controllers.ControllerFactoryProvider...
```

## Causa raiz

O host MVC possuía registros amplos em `InovaGed.Web/Program.cs`, enquanto o host API registrava apenas `DocumentAppService` e `ICurrentUser`. O registro compartilhado em `InovaGed.Infrastructure/DependencyInjection.cs` agora registra a composição central usada pelos dois hosts, incluindo `IDocumentMoveService` e `IDocumentGuardianService`.

## Correção aplicada

- `UseDefaultServiceProvider` com `ValidateScopes=true` e `ValidateOnBuild=true` está habilitado nos dois hosts.
- `WebGed.WebApi/Program.cs` usa `AddInovaGedApplication(...).AddInovaGedInfrastructure(...)`.
- A composição compartilhada registra `IDocumentMoveService` como `Scoped` para `DocumentMoveService`.
- A composição compartilhada registra `IDocumentGuardianService` como `Scoped` para `DocumentGuardianService`.

## Dependências transitivas mapeadas

- `DocumentAppService` depende de `IDocumentWriteRepository`, `IFileStorage`, `IPdfTextExtractor`, `IAuditLogWriter`, `IDocumentGuardianService` e serviços de log/framework.
- `DocumentMoveService` depende dos serviços GED/documentos registrados na infraestrutura.
- `DocumentGuardianService` depende dos serviços de Guardião registrados na infraestrutura.

## Observação de validação

A execução real de `dotnet build`, `dotnet test` e inicialização local ficou bloqueada pela ausência do comando `dotnet` no ambiente de execução do agente.
