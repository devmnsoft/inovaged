# relatorio consolidacao funcional

Consolidação funcional iniciada em 2026-07-13.

## Implementado neste ciclo

- Solution consolidada com projetos de aplicação, domínio, infraestrutura, web, WebApi e testes.
- Padrão canônico das tabelas do Guardião: `ged.document_twin`, `ged.document_finding`, `ged.document_finding_evidence`, `ged.document_relationship`.
- Migration idempotente de compatibilidade com views `document_guardian_*`, outbox persistente, fila do Guardião, dossiês, itens de Meu Trabalho e SLA.
- Infraestrutura de relógio UTC/timezone do tenant.
- Catálogo determinístico de regras GED/ARQ/SEC/LGPD/OPS/PHY.
- Cálculo explicável de risco e completude.
- Contratos de eventos internos, dossiês, manifesto, Meu Trabalho e SLA.

## Homologação mínima

1. Executar `dotnet restore InovaGed.sln`.
2. Executar `dotnet build InovaGed.sln --no-restore`.
3. Executar `dotnet test InovaGed.sln --no-build`.
4. Aplicar migrations em base PostgreSQL de homologação.
5. Validar /DocumentGuardian, /MyWork e /Dossiers com massa multi-tenant.

## Pendências controladas

- Ligar os workers existentes ao contrato multi-tenant em todos os processadores.
- Persistir avaliações completas do motor em todos os eventos de upload/OCR/preview/classificação.
- Expandir telas administrativas além das estruturas e contratos criados.
