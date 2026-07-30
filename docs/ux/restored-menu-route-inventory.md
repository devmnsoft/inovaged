# Inventário de rotas do menu restaurado

A existência abaixo foi validada estaticamente nos controllers. Autorização segue `AppMenuPolicy`; links não são renderizados fora do perfil correspondente. A validação HTTP autenticada depende do ambiente integrado e permanece coberta pelo gate de navegador existente.

| Item | Controller | Action | Policy/perfil | Existe | Restaurado | Motivo quando não restaurado |
|---|---|---|---|---|---|---|
| Dashboard | GedDashboard | Index | admin completo | sim | sim | — |
| Central Operacional | Operations | Index | admin completo | sim | sim | — |
| Qualidade Documental | DocumentQuality | Index | admin completo | sim | sim | — |
| GED / Explorer | Ged | Index | admin completo | sim | sim | — |
| Busca Hospitalar | HospitalDocuments | Index | conforme perfil | sim | sim | — |
| Busca Inteligente | SmartSearch | Index | admin/consulta | sim | sim | — |
| Uploads | GedUploads | Index | admin completo | sim | sim | — |
| Central OCR | Ocr | Index | admin completo | sim | sim | — |
| Classificação | GedClassification | Queue | admin completo | sim | sim | — |
| Pastas | Ged | Folders | admin completo | sim | sim | — |
| Localizações / Caixas | Physical | Locations / Boxes | admin completo | sim | sim | — |
| Etiquetas | Labels | Boxes | admin completo | sim | sim | — |
| Empréstimos | Loans | Index | admin/setor | sim | sim | — |
| Protocolo | Protocolo | Index | admin completo | sim | sim | — |
| Minhas Solicitações | ProtocolRequests | My | admin/arquivista | sim | sim | — |
| Administração | Administration | Index | admin completo | sim | sim | — |
| Parâmetros | Parameters | Index | admin completo | sim | sim | — |
| Usuários | Users | Index | admin completo | sim | sim | — |
| Logs | SystemLogs | Index | admin completo | sim | sim | — |
| Continuidade | Continuity | Overview | admin completo | sim | sim | — |
| SystemHealth | SystemHealth | Index | admin completo | sim | sim | — |
| Inteligência Hospitalar, Alertas e Tendências, Fila/Agendamento OCR, Dossiês, solicitar protocolo, Schema, Homologação e Configurações | diversos | diversos | a confirmar | não validado integralmente | não | omitido para não expor rota sem contrato confirmado |
