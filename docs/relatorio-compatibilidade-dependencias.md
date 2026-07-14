# Relatório de compatibilidade de dependências

- Ambiente local em 2026-07-13: `dotnet` indisponível; Docker também indisponível. A validação executável foi deslocada para CI com SDK 8.0.x.
- Target framework padrão encontrado: `net8.0` em todos os projetos.
- Ajustes aplicados para compatibilidade .NET 8:
  - `Microsoft.Extensions.Logging.Abstractions` 10.0.1 -> 8.0.2.
  - `Microsoft.AspNetCore.Cryptography.KeyDerivation` alinhado para 8.0.27.
  - `Npgsql` 10.0.0 -> 8.0.6 para alinhar com `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11.
- `WebGed.WebApi` foi incluído na solution para não permanecer órfão.
- `InovaGed.Application.Tests` foi incluído na solution para execução por `dotnet test InovaGed.sln`.
