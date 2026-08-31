# Source Code Deep Audit — mapa técnico

_Data do inventário: 2026-08-31._

## Projetos encontrados

Foram encontrados 16 projetos: os nove projetos solicitados (`Application`, `Infrastructure`, `Web`, `Environment.Doctor`, `Operations.Worker`, `WebGed.WebApi`, `Application.Tests`, `Signing.EndToEndTests` e `Portability.Verifier`) e ainda `Domain`, `Database.Migrator`, `Deployment.Tool`, seus testes, `Signing.Agent`, seus testes e `UiTests`.

## Inventário

| Área | Resultado |
|---|---:|
| Controllers Web | 108 |
| Services (Application/Infrastructure/Web) | 138 |
| Repositories Infrastructure | 29 |
| Views Razor | 487 |
| Migrations em `database/migrations` | 130 |
| Workers | 9 |
| Middlewares Web | 6 |
| TagHelpers | 2 |

Os controllers prioritários estão presentes, com `ContractFiscalizationController` representando a medição contratual. Serviços e repositórios usam contratos da camada Application; o composition root principal é `InovaGed.Web/Program.cs`. A inspeção estática e o quality check de DI existente não apontaram contrato novo sem implementação; o novo `IConsistencyAuditService` foi registrado explicitamente.

## Razor, migrations e schema

Views críticas existem em `Views/Ged`, `Labels`, `Administration`, `SmartGed`, `SmartAssistant`, `SmartWorkflow`, `Governance`, `FiscalPortal`, `Physical` e `ClassificationPlan`. As tabelas operacionais esperadas incluem `ged.document`, `ged.folder`, `ged.ocr_job`, `ged.box`, `ged.label_template`, `ged.label_print_history`, `ged.loan_request`, `ged.contract_fiscalization_period` e tabelas de workflow/governança/fiscal. O manifesto obrigatório é controlado por `database/required_migrations.json` e `database/apply_all_required_migrations.sql`; não foi introduzida migration nesta entrega.

## Rotas, infraestrutura e frontend

As rotas críticas estão inventariadas em `InovaGed.Environment.Doctor/quality-routes.json`, incluindo Dashboard, módulos operacionais, busca global e consistência. Workers incluem GED processing, OCR e stale uploads. Middlewares cobrem auditoria, incidentes, correlação, requisições suspeitas e acesso negado. Os filtros incluem tratamento de `DatabaseSchemaException`; TagHelpers Atlas fornecem ícones e ilustrações. Os ativos principais incluem `atlas-ui.css`, `ged-workspace.css`, `document-360.css`, CSS de etiquetas/administração e scripts `ged-preview.js`, `ged-workspace.js` e `atlas-forms.js`.
