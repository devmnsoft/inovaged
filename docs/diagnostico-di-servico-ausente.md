# Diagnóstico de DI — serviço ausente no WebApi

## Execução

Comando executado no ambiente do agente:

```bash
dotnet build WebGed.WebApi/WebGed.WebApi.csproj --no-restore
```

Resultado do ambiente: `dotnet: command not found`. Portanto, não foi possível executar o host neste container para capturar uma exceção real de runtime com stack trace completo, correlationId e endpoint acessado.

## Tipo ausente identificado pela análise estática

A análise dos construtores ativos do WebApi identificou que o `DocumentsController` solicita `IDocumentMoveService`, cuja implementação concreta `DocumentMoveService` solicita `IPermissionService` no construtor. O `WebGed.WebApi/Program.cs` registrava `DocumentMoveService`, mas não registrava `IPermissionService`.

Mensagem esperada quando o container tenta ativar `DocumentMoveService` sem o registro transitivo:

```text
Unable to resolve service for type 'InovaGed.Application.Security.IPermissionService'
while attempting to activate 'InovaGed.Infrastructure.Ged.Documents.DocumentMoveService'.
```

## Consumidor

* Endpoint provável: qualquer rota de `DocumentsController` que ative o controller, especialmente `POST /api/Documents/move`, `POST /api/Documents/move-bulk`, `GET /api/Documents/folders/search` ou `GET /api/Documents/{id}/move-history`.
* Controller: `WebGed.WebApi.Controllers.DocumentsController`.
* Serviço transitivo: `InovaGed.Infrastructure.Ged.Documents.DocumentMoveService`.
* Tipo solicitado: `InovaGed.Application.Security.IPermissionService`.

## Correção aplicada

* Ativada validação do provider no startup (`ValidateScopes` e `ValidateOnBuild`).
* Centralizados registros compartilhados em `AddInovaGedApplication` e `AddInovaGedInfrastructure`.
* Registrado `IPermissionService` com implementação `PermissionService` e lifetime scoped.
* Registradas dependências transitivas de `DocumentAppService` e `IDocumentMoveService`.
