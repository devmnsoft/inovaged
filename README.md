# InovaGED

Sistema GED com módulos de documentos, temporalidade, empréstimos, OCR, preview, workflow e Guardião Documental.

## Build

```bash
dotnet restore InovaGed.sln
dotnet build InovaGed.sln --no-restore
dotnet test InovaGed.sln --no-build
```

O CI em `.github/workflows/ci.yml` executa restore, build, testes, validação JSON, `git diff --check`, busca simples de segredos e validação de migrations.

## Instalação local no Windows (sem Docker)

Abra o PowerShell na raiz do repositório e execute o setup com uma connection string de um PostgreSQL local. A credencial é armazenada em .NET User Secrets, não em arquivo versionado.

```powershell
.\scripts\windows\Setup-InovaGed.ps1 `
  -ConnectionString 'Host=localhost;Port=5432;Database=inovaged;Username=inovaged;Password=<senha>' `
  -DataRoot 'C:\InovaGed' `
  -RequireOcr
```

O script valida .NET, ferramentas PostgreSQL e, quando solicitado, OCR; cria e testa diretórios, aplica migrations e restaura pacotes. Para diagnóstico isolado, use `Test-InovaGedPrerequisites.ps1`. As rotinas `Backup-InovaGed.ps1` e `Restore-InovaGed.ps1` produzem backup custom validado e exigem confirmação explícita para restauração.

Em banco novo, acrescente `-InitializeDatabase` ao [setup](scripts/windows/Setup-InovaGed.ps1). Isso executa `install --verify`: carrega o schema base sem dados e aplica o manifesto. O schema GED deve estar vazio, exceto pelo diário do migrador. Em instalações existentes, omita essa opção para executar o upgrade incremental `apply --verify`; não importe o schema base sobre dados existentes.

O [migrador](InovaGed.Database.Migrator/Program.cs) lê `ConnectionStrings__DefaultConnection`. Para uma migration transacional que falhou, `apply --retry-failed ID --verify` admite nova tentativa somente com o checksum original e mantém o registro FAILED anterior. Não apague o diário nem edite migrations publicadas para contornar uma falha. Antes de atualizar a recuperação de temporalidade, encerre os workers antigos.

Os roteiros com dados fictícios, os resultados e as limitações de homologação estão no [relatório operacional](docs/ai-operational-acceptance.md). A instalação oficial foi exercitada; isso não equivale ao aceite integral das jornadas e da interface.

## Consolidação 2026-07

- Tabelas canônicas do Guardião sem sufixo `guardian`.
- Views de compatibilidade `document_guardian_*` para consultas antigas.
- Outbox interno persistente.
- Fila persistente de avaliação do Guardião.
- Contratos para Dossiês, Meu Trabalho, SLA e Manifesto de Integridade.
- Regras determinísticas e scores explicáveis.
