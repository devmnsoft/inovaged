# Inventário funcional de rotas

Inventário da entrega **Functional Navigation Stabilization**. Os status abaixo representam o contrato validado estaticamente; a coluna “Status atual” deve ser complementada pelo relatório HTTP de cada ambiente em `artifacts/quality-gate/route-smoke-report.md`. Respostas 302, 401 e 403 são esperadas quando a sessão não possui a política exigida.

| Controller | Action | Rota | HTTP | Requer permissão? | Status esperado | Status atual | Correção aplicada |
|---|---|---|---|---|---|---|---|
| Home | Index | `/` | GET | Autenticação | 200/302/401 | Inventariada | Incluída no smoke crítico |
| Administration | Index | `/Administration` | GET | Administração | 200/302/401/403 | Inventariada | Cards validados automaticamente |
| Administration | Users | `/Administration/Users` | GET | Administração | 200/302/401/403 | Inventariada | Incluída no smoke crítico |
| Administration | Tenants | `/Administration/Tenants` | GET | Administração/full admin | 200/302/401/403 | Inventariada | Incluída no smoke crítico |
| Administration | Workers | `/Administration/Workers` | GET | Administração | 200/302/401/403 | Inventariada | Incluída no smoke crítico |
| Administration | Migrations | `/Administration/Migrations` | GET | Administração | 200/302/401/403 | Inventariada | Incluída no smoke crítico |
| Administration | Security/Permissions | `/Administration/Security`, `/Administration/Permissions` | GET | Administração | 200/302/401/403 | Inventariada | Alias de permissões validado |
| Administration | Health | `/Administration/Health` | GET | Administração | 200/302/401/403 | Inventariada | Incluída no smoke crítico |
| SchemaHealth | Index/FixScript | `/SchemaHealth`, `/SchemaHealth/FixScript` | GET | Administração | 200/302/401/403 | Inventariada | Ambos os diagnósticos no smoke |
| Labels | Index/PrintWizard/History/LocDesk | `/Labels`, `/Labels/PrintWizard`, `/Labels/History`, `/Labels/LocDesk` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Fluxo de etiquetas coberto |
| LabelTracking | Scanner/Inventory | `/LabelTracking/Scanner`, `/LabelTracking/Inventory` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Rastreamento coberto |
| Physical | Boxes | `/Physical/Boxes` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Guarda física coberta |
| Retention | Index | `/Retention` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Temporalidade coberta |
| RetentionDestination | Index | `/RetentionDestination` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Destinação coberta |
| RetentionCase | Index | `/RetentionCase` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Casos cobertos |
| InstrumentVersions | PCD/TTD/POP | `/Instruments/Versions/{PCD,TTD,POP}` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Três instrumentos no smoke |
| Loans | Index | `/Loans` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Empréstimos cobertos |
| Documents | Index | `/Documents` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Verificação estática do controller/view |
| Search | Index | `/Search` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Verificação estática do controller/view |
| SmartSearch | Index | `/SmartSearch` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Verificação estática do controller/view |
| HospitalBilling | Index | `/HospitalBilling` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Verificação estática do controller/view |
| Ocr | Index | `/Ocr` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Verificação estática do controller/view |
| Poc | Index | `/Poc` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Incluída no smoke crítico |
| Protocols | WorkQueue | `/Protocols/WorkQueue` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Fila incluída no smoke crítico |
| DocumentQuality | Index | `/DocumentQuality` | GET | Usuário autorizado | 200/302/401/403 | Inventariada | Qualidade incluída no smoke crítico |

## Regra operacional

O smoke considera **somente 200, 302, 401 e 403** respostas aceitáveis. HTTP 500, falhas de conexão e respostas contendo assinaturas de RuntimeCompilation, schema não tratado, materialização Dapper ou resolução de DI falham o gate.
