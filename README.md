# InovaGED

Sistema GED com módulos de documentos, temporalidade, empréstimos, OCR, preview, workflow e Guardião Documental.

## Build

```bash
dotnet restore InovaGed.sln
dotnet build InovaGed.sln --no-restore
dotnet test InovaGed.sln --no-build
```

O CI em `.github/workflows/ci.yml` executa restore, build, testes, validação JSON, `git diff --check`, busca simples de segredos e validação de migrations.

## Consolidação 2026-07

- Tabelas canônicas do Guardião sem sufixo `guardian`.
- Views de compatibilidade `document_guardian_*` para consultas antigas.
- Outbox interno persistente.
- Fila persistente de avaliação do Guardião.
- Contratos para Dossiês, Meu Trabalho, SLA e Manifesto de Integridade.
- Regras determinísticas e scores explicáveis.
