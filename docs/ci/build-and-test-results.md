# Resultados locais de build e testes

Ambiente de análise em 2026-07-27:

- o SDK `dotnet` não está instalado;
- o GitHub CLI não está instalado;
- a rede recusou downloads de Go modules e logs do GitHub (HTTP 403).

Nenhum resultado verde foi declarado artificialmente. Build, testes, startup, Windows/DPAPI e gates remotos continuam pendentes de execução no GitHub Actions. O workflow publica TRX e rejeita execução com zero testes.
