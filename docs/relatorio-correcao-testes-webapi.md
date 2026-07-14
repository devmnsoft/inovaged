# Relatório de correção dos testes e do WebApi

Data: 2026-07-14

## Causas identificadas

- O projeto `InovaGed.Application.Tests` não habilitava `ImplicitUsings` nem declarava `IsTestProject`, deixando tipos básicos do `System` e a descoberta de testes dependentes de imports arquivo a arquivo.
- O projeto de testes possuía xUnit como pacote, mas não tinha `global using Xunit`, o que fazia atributos como `[Fact]`, `[Theory]` e `[MemberData]` falharem quando os arquivos não importavam xUnit explicitamente.
- Os cenários SQL de protocolo usavam `IEnumerable<object[]>` e datas dinâmicas (`DateTimeOffset.UtcNow`), aumentando o risco de erro do analyzer xUnit e reduzindo determinismo.
- A fake `ThrowingDbConnectionFactory` não retornava uma `Task<NpgsqlConnection>` compatível por falha assíncrona explícita, embora a interface real exija `Task<NpgsqlConnection> OpenAsync(CancellationToken)`.
- O `WebGed.WebApi` referenciava namespaces legados/inexistentes (`WebGed.Application.Common` e `InovaGed.Infrastructure.Database`).
- `CurrentUser` usava `Guid.Parse` e `HttpContext!`, podendo gerar `NullReferenceException` ou `FormatException` em claims ausentes/inválidas, além de não implementar `Roles` da interface atual.
- O WebApi buscava `ConnectionStrings:Default`, enquanto o restante da solução documenta e usa `ConnectionStrings:DefaultConnection`.

## Arquivos alterados

- `InovaGed.Application.Tests/InovaGed.Application.Tests.csproj`
- `InovaGed.Application.Tests/GlobalUsings.cs`
- `InovaGed.Application.Tests/ProtocolRequestServiceSqlTests.cs`
- `InovaGed.Application.Tests/Infrastructure/Sql/ProtocolRequestServiceSqlTests.cs`
- `InovaGed.Application.Tests/SmartQueryParserDateTests.cs`
- `InovaGed.Application.Tests/StartupConfigurationValidatorTests.cs`
- `InovaGed.Application.Tests/Security/CurrentUserTests.cs`
- `InovaGed.Application.Tests/WebApi/WebApiConfigurationTests.cs`
- `WebGed.WebApi/Security/CurrentUser.cs`
- `WebGed.WebApi/Program.cs`
- `docs/relatorio-correcao-testes-webapi.md`

## Testes corrigidos e adicionados

- Corrigidos os testes de SQL de protocolo para usar `TheoryData<ProtocolWorkQueueFilter, ProtocolVisibilityScope>`.
- Mantidos cenários de filtro vazio, status, pesquisa, período, administrador com `ShowAll`, administrador Ophir, prioridade, `OnlyMine`, vencido e devolvido para ajuste.
- Substituídas datas dinâmicas por `FixedNow = 2026-07-14T12:00:00Z`.
- Corrigida a fake `ThrowingDbConnectionFactory` de testes do parser de datas para respeitar o contrato real de `IDbConnectionFactory`.
- Adicionados testes para leitura válida de claims do `CurrentUser`, tenant ausente, usuário ausente e GUID inválido.
- Adicionado teste de configuração do WebApi para erro claro quando `ConnectionStrings:DefaultConnection` não está configurada.

## Namespaces corrigidos

- `WebGed.Application.Common` foi substituído por `InovaGed.Application.Identity` em `WebGed.WebApi/Security/CurrentUser.cs`.
- `InovaGed.Infrastructure.Database` foi substituído por `InovaGed.Infrastructure.Common.Database` em `WebGed.WebApi/Program.cs`.
- A busca `rg -n "WebGed\.Application|InovaGed\.Infrastructure\.Database" --glob '*.cs'` não retornou usos remanescentes em arquivos C#.

## Resultado do build e testes

A validação solicitada foi executada, mas o ambiente não possui o SDK/CLI do .NET instalado:

```text
/bin/bash: line 1: dotnet: command not found
```

Por isso, não foi possível obter neste container:

- build do projeto de testes;
- build do `WebGed.WebApi`;
- build da solution;
- descoberta efetiva de testes;
- quantidade final de testes aprovados/falhos.

## Quantidade de testes descobertos

Não disponível neste ambiente porque `dotnet test` não pôde ser executado sem o comando `dotnet`.

## Testes aprovados e falhos

Não disponível neste ambiente porque `dotnet test` não pôde ser executado sem o comando `dotnet`.

## Observação sobre a fase 2

A fase 2 não foi iniciada porque o critério do próprio pedido exige que a evolução só comece após a solution compilar e os testes serem descobertos corretamente. Essa validação está bloqueada pela ausência do SDK .NET no ambiente de execução.
