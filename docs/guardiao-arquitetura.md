# Arquitetura do InovaGED Guardião

O Guardião é um módulo desacoplado, determinístico e sem dependência externa de IA nesta entrega. A rota `/DocumentGuardian/{documentId}` consulta dados reais persistidos no schema `ged` por Dapper.

## Componentes
- `IDocumentGuardianService`: contrato de leitura do gêmeo digital.
- `DocumentGuardianService`: consultas Dapper, linha do tempo e auditoria de acesso.
- `DocumentGuardianController`: rota MVC protegida por `GedAccess`.
- `Views/DocumentGuardian/Details.cshtml`: visual responsivo com scores, alertas, evidências, relacionamentos, obrigações, decisões e timeline.
- Migration `2026_07_document_guardian.sql`: tabelas idempotentes.

## Modelo de dados
Inclui twin, relacionamentos, evidências, regras versionadas, findings, evidências de findings, decisões humanas, obrigações, perfis/requisitos de completude e quebra de vidro.

## Princípios
- Nenhum alerta sem evidência persistida.
- Todas as datas operacionais são UTC.
- Todas as tabelas pertinentes têm `tenant_id`.
- Auditoria registra acesso ao Guardião.
- Rollback é lógico/documentado, sem `DROP` automático.
