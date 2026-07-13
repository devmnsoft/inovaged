# Diagnóstico de consolidação — julho de 2026

Data da execução: 2026-07-13.
Branch local: `work`.

## Escopo validado antes de alterações funcionais

Foram lidos os documentos e artefatos obrigatórios existentes no repositório: `README.md`, arquitetura, diagnóstico técnico do Guardião, documentação funcional e de homologação do Guardião, configuração, workers multi-tenant, `Program.cs`, projetos `.csproj`, migrations de julho de 2026, scripts consolidados e arquivos de relógio/timezone/workers/Guardião localizados no código.

## Bloqueio de ambiente

A validação .NET obrigatória não pôde ser concluída porque o SDK .NET não está instalado no ambiente de execução.

Comandos executados:

```bash
dotnet --info
```

Resultado:

```text
/bin/bash: line 1: dotnet: command not found
```

Também foi tentada a preparação do SDK .NET 8 pelo instalador oficial, porém o download foi bloqueado pela rede do ambiente:

```text
curl: (56) CONNECT tunnel failed, response 403
```

Por esse motivo, seguindo a exigência explícita da consolidação, nenhuma implementação funcional profunda foi avançada como se o build estivesse validado.

## Atualização da branch a partir de main

A tentativa de atualização por `origin/main` falhou porque o repositório local não possui remote `origin` configurado:

```text
fatal: 'origin' does not appear to be a git repository
fatal: Could not read from remote repository.
```

## Achados iniciais

### Build e testes

- Build não validado por ausência de SDK .NET.
- Testes não executados por ausência de SDK .NET.
- A solution `InovaGed.sln` referencia apenas os projetos principais e não referencia `InovaGed.Application.Tests`, logo os testes podem não ser executados por `dotnet test InovaGed.sln` mesmo quando o SDK estiver disponível.

### Dependências e projetos

- Os projetos estão em `net8.0`.
- Há referências a pacotes `Microsoft.Extensions.*` versão `10.0.1` em projetos `net8.0`; isso deve ser confirmado em restore/build com SDK disponível.
- `WebGed.WebApi` existe no repositório, mas não está incluído na solution principal.

### Abstrações de relógio e timezone

- Foram encontrados `IClock` e `SystemClock` como abstração/implementação principais.
- Foi encontrado teste `TenantTimeZoneServiceTests.cs` para timezone.
- Não foram localizadas, nesta varredura inicial, todas as duplicidades citadas na solicitação (`IApplicationClock`, `SystemApplicationClock`, `ITenantTimeZoneProvider`, etc.); a confirmação completa depende de build e análise pós-restore.
- Ainda há ocorrências de `DateTimeOffset.UtcNow` em código de infraestrutura, por exemplo em preview e empréstimos, que precisam ser migradas de forma segura para a abstração consolidada.

### Workers

- O diagnóstico anterior já registrava workers com tenant fixo em configuração (`Workers:LoanOverdue:TenantId`, `DocumentQuality:TenantId`, `OcrAutoSchedule:TenantId`).
- `LoanOverdueWorker` ainda contém fallback para `Workers:LoanOverdue:TenantId` e precisa ser migrado para enumeração real de tenants ativos.
- Há filas em memória/processamento por job com `TenantId`, mas a validação de isolamento multi-tenant requer testes com dois tenants.

### Migrations

- Existem migrations de julho de 2026 para Guardião e estabilização de configuração.
- O script `database/apply_all_required_migrations.sql` deve permanecer como orquestrador de ordem de aplicação.
- A validação de idempotência SQL foi limitada à inspeção textual; não houve aplicação em PostgreSQL neste ambiente.

### Guardião

- O Guardião possui modelos, contrato de serviço de leitura, implementação de infraestrutura, controller e view de detalhes.
- O motor operacional de avaliação determinística, fila persistente, decisões humanas completas e score explicável ainda precisam ser validados/implementados com build disponível.

### Telas sem backend completo / funções parciais

- O Guardião aparenta ter camada de leitura e tela, mas não foi possível confirmar execução ponta a ponta.
- A Central do Guardião, Meu Trabalho, fila persistente e acesso emergencial funcional precisam ser verificados após ambiente .NET operacional.

## Itens que devem ser retomados assim que o SDK estiver disponível

1. Executar `dotnet --info` e registrar a versão.
2. Executar `dotnet clean InovaGed.sln`.
3. Executar `dotnet restore InovaGed.sln`.
4. Executar `dotnet build InovaGed.sln --no-restore`.
5. Incluir `InovaGed.Application.Tests` na solution se a intenção for cobrir os testes pelo comando da solution.
6. Executar `dotnet test InovaGed.sln --no-build`.
7. Só então avançar em alterações funcionais amplas.
