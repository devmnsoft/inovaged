# Relatório — correção de DI

## Serviço ausente

- Contrato: `IDocumentMoveService`
- Implementação: `DocumentMoveService`
- Consumidor: `WebGed.WebApi.Controllers.DocumentsController`
- Lifetime: `Scoped`

## Correção

A composição central está em `InovaGed.Infrastructure/DependencyInjection.cs` e é consumida pelos hosts MVC e API. O host API deixou de depender de uma composição mínima e passou a usar os módulos compartilhados de Application e Infrastructure.

## Validação do container

Os hosts `InovaGed.Web` e `WebGed.WebApi` habilitam `ValidateScopes` e `ValidateOnBuild` no `UseDefaultServiceProvider`, antecipando falhas de composição durante a inicialização.

## Limitação de ambiente

A validação executável ficou limitada porque o container não possui `dotnet` instalado.
