# Matriz de consolidação funcional do InovaGED

## Critérios

- **Completo**: fluxo implementado, integrado, persistido e testado.
- **Funcional com pendências**: fluxo utilizável, mas com lacunas de robustez, automação, UX, métricas ou testes.
- **Parcial**: contratos/modelos ou partes do fluxo existem, mas faltam integrações centrais.
- **Somente tela**: há UI sem backend consolidado.
- **Somente infraestrutura**: há tabelas/serviços base sem jornada de usuário final.
- **Ausente**: não identificado no código atual.

## Classificação por módulo

| Módulo | Estado | Evidências | Pendências principais |
|---|---|---|---|
| GED | Funcional com pendências | Controladores, serviços de documentos, pastas, movimentação, busca e dashboard. | Consolidar rollback/versionamento completo, manifesto e testes ponta a ponta. |
| Upload | Funcional com pendências | Upload simples, lote e chunk possuem contratos/controladores. | Retomada/cancelamento/limpeza e cobertura completa de duplicidade/rollback. |
| OCR | Funcional com pendências | Serviços, filas, scheduler, status e dashboard. | Backoff/retry padronizado, qualidade por versão e reprocessamento integrado ao Guardião. |
| Preview | Parcial | Interfaces de preview, status e geração via LibreOffice. | Timeout, fallback por tipo, reprocessamento e vínculo formal por versão. |
| Classificação | Funcional com pendências | Sugestores, regras, auditoria e pendências de classificação. | Regras mais completas, aprovação/versionamento e métricas. |
| PCD | Parcial | Instrumentos arquivísticos e planos existem. | Completar governança, versionamento operacional e relatórios normativos. |
| TTD | Parcial | Retention/temporalidade e destination models existem. | Cálculo e destinação com aprovações, termos e holds completos. |
| Temporalidade | Funcional com pendências | Casos, filas, termos e controllers de retenção. | Eliminação/recolhimento com manifesto e trilha probatória completa. |
| Assinatura digital | Parcial | Controllers e views de assinatura individual/lote. | Cadeia, revogação, relatório, exportação e auditoria ampliada. |
| Empréstimos | Funcional com pendências | Loans, solicitações, histórico, links seguros e SQL tests. | QR Code, perda/dano, cobrança automática e SLA. |
| Caixas | Funcional com pendências | Physical/boxes e histórico. | Integração com inventário, etiquetas e auditoria de movimentação. |
| Localização física | Funcional com pendências | Physical map/location forms. | Validações de localização obrigatória e reconciliação com documentos físicos. |
| Protocolo | Funcional com pendências | Protocol controllers/services/tests. | Integração completa com workflow, SLA, dossiês e manifesto. |
| Workflow | Parcial | DocumentWorkflow e controllers dedicados. | Paralelismo, gatilhos, escalonamento, anexos obrigatórios e integração com Meu Trabalho. |
| Auditoria | Funcional com pendências | Audit services, dashboards, security audit. | Linha do tempo probatória unificada e exportável. |
| PACS | Parcial | Tickets, integração PACS e API. | Quarentena, retentativas, erro operacional e notificações persistidas. |
| Guardião | Funcional com pendências | Models, regras, scoring, fila e testes de regras/evidências. | Worker multi-tenant resiliente, dead-letter operacional e dashboard executivo completo. |
| Busca | Funcional com pendências | GED search, smart search e parser. | Consulta unificada com tenant, sigilo e permissões em todas as entidades. |
| Relatórios | Parcial | Report DTOs e relatórios de destinação. | Painéis executivos e exportações auditáveis por módulo. |
| System Health | Funcional com pendências | Controllers de health/schema/homologação. | Checks automatizados, alertas persistidos e CI operacional ampliado. |
| Central de Pendências | Parcial | Há pendências distribuídas por OCR/classificação/retention/Guardian. | Criar central única com filtros, atribuição, comentários e ações em lote. |
| Meu Trabalho | Parcial | Modelos em `InovaGed.Application/MyWork`. | Criar rota `/MyWork`, agregação por usuário/grupo, SLA e histórico. |
| Dossiês inteligentes | Parcial | Modelos em `InovaGed.Application/Dossiers`. | Persistência, UI, completude, risco, timeline, pendências e manifesto. |
| Motor de completude | Parcial | Scores do Guardião e modelos de qualidade. | Perfis configuráveis por requisito e validação por tipo/metadado/assinatura/prazo. |
| Notificações persistidas | Parcial | Interfaces de notificação/SignalR. | Persistência, leitura, agrupamento, retentativa, e-mail configurável e deduplicação. |
| Timeline probatória | Parcial | Auditoria e eventos espalhados por módulo. | Componente reutilizável por documento/dossiê/processo/prontuário. |
| Manifesto de integridade | Parcial | Modelos de manifesto existem. | Geração JSON/PDF/ZIP com hashes, versões, assinaturas e trilha de auditoria. |
| Dashboard executivo | Parcial | Dashboards GED/OCR/auditoria/operações. | Indicadores reais consolidados e filtrados por tenant/permissão. |

## Próximo ciclo recomendado

1. Executar CI com SDK .NET 8 para confirmar restore/build/test.
2. Implementar Central de Pendências como agregador de fontes já existentes.
3. Publicar `/MyWork` consumindo pendências e workflow.
4. Consolidar Dossiês + Manifesto + Timeline em uma fatia vertical.
5. Fechar Guardião com worker, dead-letter e dashboard operacional.
