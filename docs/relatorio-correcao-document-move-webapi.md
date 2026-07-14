# Relatório de correção — Document Move WebApi

## Causa

O `WebGed.WebApi/Controllers/DocumentsController.cs` ainda chamava `IDocumentMoveService` com assinaturas antigas. A interface atual exige `userId` em `SearchFoldersAsync`, além de `userName`, `source`, `isAdmin` e `CancellationToken` em `MoveAsync` e `MoveBulkAsync`. Por isso o WebApi falhava com CS7036 e os projetos dependentes podiam falhar com CS0006 porque a DLL do WebApi não era gerada.

## Chamadas corrigidas

- `SearchFoldersAsync` agora recebe `tenantId` e `userId` do `ICurrentUser`.
- `MoveAsync` agora recebe tenant, usuário, nome/e-mail, documento, destino, motivo, origem controlada, `isAdmin` calculado por roles administrativas e o `CancellationToken` da requisição.
- `MoveBulkAsync` agora recebe os mesmos dados contextuais e valida previamente lista, duplicidades, limite, destino, motivo e origem.
- `GetMoveHistoryAsync` usa tenant do usuário autenticado, sem leitura manual de claims.

## Roles administrativas usadas

A pesquisa por roles no projeto indicou como roles administrativas reais:

- `ADMIN`;
- `ADMINISTRADOR`;
- `ADMINISTRADOROPHIR`.

A regra foi centralizada em `DocumentMoveAuthorizationRoles` para evitar strings espalhadas pelo controller.

## Arquivos alterados

- `WebGed.WebApi/Controllers/DocumentsController.cs`;
- `InovaGed.Application/Ged/Documents/DocumentMoveAuthorizationRoles.cs`;
- `InovaGed.Application/Ged/Documents/DocumentMoveDtos.cs`;
- `InovaGed.Application.Tests/WebApi/DocumentsControllerMoveTests.cs`;
- `.github/workflows/ci.yml`;
- `.github/workflows/dotnet-ci.yml`;
- `docs/relatorio-correcao-document-move-webapi.md`.

## Testes

Foram adicionados testes de unidade para o `DocumentsController` cobrindo:

- propagação de tenant e usuário em busca de pastas;
- `isAdmin=false` para usuário comum;
- `isAdmin=true` para administrador;
- propagação de `CancellationToken`;
- validação de lote vazio;
- rejeição de IDs duplicados;
- mapeamento de `ACCESS_DENIED`, `DOCUMENT_NOT_FOUND` e `CONFLICT`;
- resposta 200 em sucesso;
- garantia de que tenant e usuário vêm do `ICurrentUser`, não do body.

## Build

A correção não cria DLL manualmente nem remove referência ao WebApi. O CS0006 é tratado como consequência da correção do build do WebApi.

## Riscos restantes

- O ambiente local desta execução não possui o comando `dotnet` disponível no PATH; a validação completa deve rodar no CI ou em ambiente com SDK .NET 8 instalado.
- A fase evolutiva ampla de preview, undo, Guardião, workflow e notificações foi preparada parcialmente com contratos de request e guardas de CI, mas demanda ciclo dedicado para implementação completa e migrações de persistência.
