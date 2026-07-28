# Inventário inicial de telas e rotas

Levantamento estático executado sobre controllers e views. “A verificar” significa que o comportamento precisa de teste integrado antes de ser promovido a evidência.

| Tela | Controller/action | Rota | View | Policy/roles atuais | Módulo/menu | Estados visual/responsivo/vazio/erro | Ações mutáveis / antiforgery |
|---|---|---|---|---|---|---|---|
| Login | Account/Login | `/Account/Login` | Account/Login | anônimo | autenticação, fora do menu | layout dedicado; responsivo; validação MVC | login POST / token MVC |
| Esqueci senha | Account/ForgotPassword | `/Account/ForgotPassword` | Account/ForgotPassword | anônimo | autenticação | dedicado; estados por formulário | POST / token MVC |
| Dashboard GED | GedDashboard/Index | `/GedDashboard` | GedDashboard/Index | autenticado/admin | Visão Geral / Dashboard | KPIs; responsividade a verificar | consulta |
| GED Explorer | Ged/Index | `/Ged` | Ged/Index | FullAdmin | Documentos / GED / Explorer | tabela/árvore/painel; móvel parcial | pasta, upload, OCR, classificação; cobertura mista a auditar |
| Upload | Ged/Create | `/Ged/Create` | Ged/Create | criação documental | Documentos / Uploads | formulário | POST / token MVC |
| OCR | Ocr/Index | `/Ocr` | Ocr/Index | admin | Documentos / Central OCR | fila e erro | POSTs a verificar |
| Classificação | GedClassification/Queue | `/GedClassification/Queue` | GedClassification/Queue | admin | Documentos / Classificação | fila/vazio | classificação rápida requer auditoria transversal |
| Busca Hospitalar | HospitalDocuments/Index | `/HospitalDocuments` | HospitalDocuments/Index | perfis hospitalares/admin | Documentos | busca/vazio/erro | consulta |
| PCD/TTD | ClassificationPlan/Index | `/ClassificationPlan` | ClassificationPlan/Index | admin | não exposto no menu atual | árvore; responsividade a verificar | Move POST: antiforgery a confirmar |
| POP | Pop/Index | `/Pop` | Pop/Index | admin | não exposto | a verificar | a verificar |
| Temporalidade | Retention/Index | `/Retention` | Retention/Index | admin | não exposto | filas/janelas | exportação e processamento a auditar |
| Empréstimos | Loans/Index | `/Loans` | Loans/Index | por perfil | Operação | lista/vazio | POSTs MVC |
| Protocolo | Protocolo/Index | `/Protocolo` | Protocolo/Index | admin | Operação | lista | POSTs MVC |
| Lotes/Dossiês | Batches/Index | `/Batches` | Batches/Index | admin | Documentos / Dossiês | lista/vazio | a verificar |
| Localizações | Physical/Locations | `/Physical/Locations` | Physical/Locations | admin | Guarda Física | lista | POSTs MVC |
| Caixas | Physical/Boxes | `/Physical/Boxes` | Physical/Boxes | admin | Guarda Física | lista | POSTs MVC |
| Etiquetas | Labels/Boxes | `/Labels/Boxes` | Labels/Boxes | admin | Guarda Física | impressão | geração/ impressão a verificar |
| Assinaturas | Signature/Index | `/Signature` | Signature/Index | autenticado | não exposto no menu atual | a verificar | registro interno ainda requer auditoria de identidade |
| Auditoria | SystemLogs/Index | `/SystemLogs` | SystemLogs/Index | admin | Administração / Logs | lista/filtro | consulta/exportação a verificar |
| Administração | Administration/Index | `/Administration` | Administration/Index | admin | Administração | atalhos | mutações em controllers próprios |
| Continuidade | Continuity/Overview | `/Continuity/Overview` | Continuity/Overview | admin | Administração | status | operações protegidas a verificar |
| SystemHealth | SystemHealth/Index | `/SystemHealth` | SystemHealth/Index | admin | Administração | saúde/erro | consulta |

## Achados

* O Dashboard apontava para `Home/Index`, cujo destino administrativo era `/Ged`; agora aponta diretamente a `/GedDashboard`.
* PCD/TTD, POP, Temporalidade e Assinaturas possuem superfície, mas não aparecem no menu administrativo atual.
* O logout aparecia na sidebar e no menu do usuário; a sidebar foi removida como segundo ponto.
* A view do Explorer e chamadas JSON autenticadas permanecem como frentes prioritárias de componentização e antiforgery.
