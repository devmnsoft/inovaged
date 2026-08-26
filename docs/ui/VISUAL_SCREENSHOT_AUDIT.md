# Visual QA Screenshot Pass — Administração e Etiquetas

**Entrega:** Visual QA Screenshot Pass - Administração e Etiquetas
**Matriz:** desktop 1366×768 e 1920×1080; mobile 390×844.
**Evidência:** `artifacts/visual-qa/screenshots`. A captura requer aplicação e banco locais, além de uma sessão administrativa conforme `tools/visual-qa/README.md`.

## Critérios usados

Hierarquia visual, espaçamento, alinhamento, contraste, botões, semântica dos ícones, tabelas, cards, empty states, responsividade e coerência com a identidade InovaGED. Também são verificados redirecionamento indevido ao login, status HTTP 500+, overflow do shell, scroll das tabelas e escala do preview.

| Rota | Screenshot gerado | Problemas visuais encontrados | Correções feitas | Desktop | Mobile | Pendências |
|---|---|---|---|---|---|---|
| `/Administration` | `administration-index-desktop*.png`, `administration-index-mobile.png` | Central com leitura pouco executiva. | Hero, status do ambiente, grupos, cards, badges e ações rápidas. | Pronto para captura | Pronto para captura | Validar métricas com dados de homologação. |
| `/Administration/Users` | `administration-users-desktop*.png` | Tabela densa e sem filtro próximo. | Breadcrumb, toolbar contextual, shell, status e empty state. | Pronto para captura | Responsivo por contrato | Validar grande volume real. |
| `/Administration/Tenants` | `administration-tenants-desktop*.png` | Hierarquia uniforme para dados distintos. | Toolbar, tabela com scroll e status destacado. | Pronto para captura | Responsivo por contrato | Conferir perfil full admin. |
| `/Administration/Security` | `administration-security-desktop*.png` | Catálogo sem contexto de filtragem. | Busca, ajuda de escopo, painéis separados e badges. | Pronto para captura | Responsivo por contrato | Revisar textos com Segurança. |
| `/Administration/Migrations` | `administration-migrations-desktop*.png` | Estado vazio técnico. | Empty state orientativo, toolbar e tabela encapsulada. | Pronto para captura | Responsivo por contrato | Exercitar schema parcialmente migrado. |
| `/Administration/Workers` | `administration-workers-desktop*.png` | Listagem pouco integrada à governança. | Cabeçalho, filtro, status e tabela responsiva. | Pronto para captura | Responsivo por contrato | Conferir estados degradados. |
| `/Labels` | `labels-index-desktop*.png` | Ações sem leitura de central operacional. | Hero, oito cards com disponibilidade e orientação pré-impressão. | Pronto para captura | Cards em uma coluna | Validar permissões de destinos. |
| `/Labels/PrintWizard` | `labels-print-wizard-desktop*.png`, `labels-print-wizard-mobile.png` | Formulário sem progressão clara. | Stepper de cinco etapas, cards, preview sticky, resumo e rodapé. | Pronto para captura | Preview e ações empilhados | Validar catálogo de tenant. |
| `/Labels/LocDesk` | `labels-locdesk-desktop*.png`, `labels-locdesk-mobile.png` | Formulário longo e preview separado apenas na etapa final. | Hero, separação formulário/suporte, ações e CSS de impressão preservado. | Pronto para captura | Colunas empilhadas | Comparação milimétrica exige amostra física. |
| `/Labels/History` | `labels-history-desktop*.png` | Sem leitura rápida e sem motivo visível. | Cinco KPIs, filtros, tabela premium, status, usuário, template e motivo. | Pronto para captura | Scroll horizontal | Filtro de período ainda é apenas visual. |
| `/Labels/Boxes` | `labels-boxes-desktop*.png` | Densidade em listas grandes. | Shell premium, badges, hover e scroll. | Pronto para captura | Scroll horizontal | Exercitar 300 registros. |
| `/Labels/Documents` | `labels-documents-desktop*.png` | Títulos longos comprimiam ações. | Tabela responsiva e ações agrupadas. | Pronto para captura | Scroll horizontal | Exercitar títulos extremos. |
| `/SystemIncidents` | `system-incidents-desktop*.png` | Consistência cruzada necessária. | Incluída na matriz automatizada de regressão HTTP/visual. | Auditoria automatizada | Não priorizado | Capturar com incidente real sanitizado. |
| `/DatabaseReadiness` | `database-readiness-desktop*.png` | Consistência cruzada necessária. | Incluída na matriz automatizada. | Auditoria automatizada | Não priorizado | Exercitar banco degradado. |
| `/SchemaHealth` | `schema-health-desktop*.png` | Consistência cruzada necessária. | Incluída na matriz automatizada. | Auditoria automatizada | Não priorizado | Exercitar divergência real. |

## Antes e depois

O estado **antes** está documentado pelos problemas da tabela e pelo relatório anterior `VISUAL_AUDIT_ADMIN_LABELS.md`. O estado **depois** é produzido com nomes determinísticos pela rotina de captura; duas larguras desktop permitem auditar densidade e espaço em telas grandes sem substituir a evidência de 1366 px. Não são versionadas imagens fabricadas: a pasta contém apenas o marcador até a execução contra um ambiente autenticado real.

## Impressão LocDesk

O CSS de impressão permanece externo e conserva borda preta, logo, QR Code, campos alinhados e destaques vermelhos para controle e volume. O texto homologado **ARQUIVO LOCDESCK ANANINDEUA** não foi alterado. Toolbar e navegação continuam removidas somente em `@media print`.

## Atlas UI Expansion — módulos operacionais (2026-08-26)

As rotas operacionais encontradas foram adicionadas a `tools/visual-qa/routes.json`. A rota `/PostGoLive` não foi encontrada e não foi criada artificialmente. A captura automatizada ficou pendente porque o SDK .NET e uma sessão autenticada não estão disponíveis no ambiente atual; os artefatos existentes não foram substituídos por imagens sem conteúdo real.
