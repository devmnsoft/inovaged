# Diagnóstico de DI — serviço ausente

Data: 2026-07-14

## Tentativa de reprodução

Comando executado a partir da raiz do repositório:

```bash
dotnet run --project WebGed.WebApi/WebGed.WebApi.csproj --no-restore
```

Resultado no ambiente atual:

```text
/bin/bash: line 1: dotnet: command not found
```

O runtime/SDK do .NET não está instalado neste container, portanto não foi possível capturar a exceção completa em execução, nem obter `correlationId` e endpoint real via HTTP.

## Identificação estática do grafo que causaria falha

O `WebGed.WebApi` registrava diretamente apenas:

- `ICurrentUser -> CurrentUser`;
- `IDbConnectionFactory -> NpgsqlConnectionFactory`;
- `DocumentAppService`;
- `IAuditWriter -> AuditWriter`;
- `IDocumentMoveService -> DocumentMoveService`.

Entretanto, o controller ativo `DocumentsController` solicita `DocumentAppService` e `IDocumentMoveService`. A criação transitiva de `DocumentAppService` exige serviços que não estavam registrados no `WebGed.WebApi`:

```text
Unable to resolve service for type 'InovaGed.Application.IPreviewGenerator'
while attempting to activate 'InovaGed.Application.Documents.DocumentAppService'.
```

Após esse primeiro tipo, também estavam ausentes no grafo transitivo:

- `InovaGed.Application.IPdfTextExtractor`;
- `InovaGed.Application.Auditing.IDocumentWriteRepository`;
- `InovaGed.Application.Auditing.IAuditLogWriter`;
- `InovaGed.Application.Common.Storage.IFileStorage`;
- `InovaGed.Application.Security.IPermissionService` para `DocumentMoveService`.

## Consumidor

- Consumidor HTTP: `WebGed.WebApi.Controllers.DocumentsController`.
- Serviço em ativação: `InovaGed.Application.Documents.DocumentAppService`.
- Endpoint provável: qualquer endpoint de `DocumentsController`; especialmente `POST /api/documents/upload` por depender diretamente de `DocumentAppService.UploadAsync`.
- `correlationId`: indisponível no ambiente atual porque a aplicação não pôde ser executada sem o SDK/runtime do .NET.

## Correção aplicada

Os registros centrais foram movidos para composição compartilhada:

- `AddInovaGedApplication(IConfiguration)`;
- `AddInovaGedInfrastructure(IConfiguration)`.

O `WebGed.WebApi` agora usa `ValidateScopes=true` e `ValidateOnBuild=true` no host para falhar durante o startup quando algum registro obrigatório estiver ausente ou quando houver dependência singleton -> scoped.
