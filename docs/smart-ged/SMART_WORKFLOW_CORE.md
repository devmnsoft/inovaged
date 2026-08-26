# Smart Workflow Core

## Objetivo
Transformar pendências documentais em tarefas operacionais rastreáveis, sem executar decisões críticas.

## Fluxo operacional
O motor detecta uma pendência, cria uma tarefa sem duplicidade, o operador assume e inicia, revisa a origem e conclui ou cancela. Cada transição produz um evento.

## Tabelas
`smart_workflow_task`, `smart_workflow_event`, `smart_workflow_rule` e `smart_workflow_dashboard_snapshot`, todas no schema `ged`.

## Tipos de tarefa
Incluem revisão de sugestões de classificação e temporalidade, qualidade, indicadores sensíveis, incidentes e ações do assistente, além dos tipos reservados para OCR, etiquetas e localização.

## Status
`OPEN`, `IN_PROGRESS`, `WAITING`, `COMPLETED`, `CANCELLED` e `REJECTED`.

## Prioridades e SLA
CRITICAL: 4h; HIGH: 8h; MEDIUM: 48h; LOW: 120h. A interface distingue no prazo, vence hoje, atrasada e sem SLA.

## Motor de regras
Consulta somente fontes locais do tenant. Uma tarefa ativa com a mesma origem, identificador e tipo impede duplicação.

## Integrações
A central é acessível pelo GED Inteligente, Assistente Documental, Administração e menu Inteligência.

## Segurança
Toda consulta e mutação usa `tenant_id`; controllers exigem autenticação e POSTs usam antiforgery. Indicadores sensíveis não são copiados. Classificação, temporalidade, descarte, ações sugeridas e incidentes nunca são decididos automaticamente.

## Como validar
Aplique a migration em Database Readiness; gere tarefas; confira lista, detalhe, SLA e histórico; execute assumir, iniciar, concluir e cancelar; valide Schema Health e compile a solução.

## Pendências futuras
Calendários úteis de SLA, escalonamento notificável, distribuição por equipes e snapshots agendados.
