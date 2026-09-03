# UI RC16 — diagnóstico de design

## Rotas e renderização real

| Rota | View efetiva | CSS carregado antes da correção | Diagnóstico |
|---|---|---|---|
| `/Labels/History` | `InovaGed.Web/Views/Labels/History.cshtml`, retornada por `LabelsController.History` | `labels-premium.css` | A view já continha uma primeira camada Atlas, mas não carregava uma folha própria; faltavam logo, quatro ações explícitas e acabamento responsivo do histórico. |
| `/Labels/PrintWizard` | `InovaGed.Web/Views/Labels/PrintWizard.cshtml`, retornada pelo fluxo de `LabelsController` | `pages/labels-print-wizard.css`, `labels-premium.css` e `labels-demo.css` | A conferência estava escrita em uma única linha na view, misturava formulário, resumo e rodapé e repetia ações na lateral. |
| `/Administration` | `InovaGed.Web/Views/Administration/Index.cshtml`, alimentada por `AdministrationController.Index` | `pages/administration.css` e `administration-premium.css` | O dashboard premium existia, mas as categorias e indicadores não correspondiam integralmente às áreas operacionais solicitadas. |
| `/ClassificationPlan` | `InovaGed.Web/Views/ClassificationPlan/Index.cshtml`, retornada por `ClassificationPlanController.Index` | `classification-plan.css` | Hero e atalhos existiam, mas faltavam cabeçalho dos cards, ações no hero e empty state orientado. |

## CSS, layout e componentes

- `labels-history.css` e `labels-printwizard.css` não existiam e, portanto, não poderiam estar referenciados.
- `administration-premium.css` e `classification-plan.css` já existiam e estavam carregados.
- Não foi encontrado partial Atlas alternativo sendo ignorado para a conferência final; foi criado `_PrintWizardFinalReview.cshtml` para separar o componente real.
- As quatro views usam o layout compartilhado atual e o workspace amplo; não foi encontrado layout legado específico nessas rotas.
- Nenhuma regra `@media` foi mantida nas views: responsividade reside em arquivos CSS.

## Componentes substituídos e correções

- Histórico: KPI cards enriquecidos, filtro de status conectado ao backend, table shell com logo/status e action bar por linha, empty state e limite explícito de 500.
- SQL do histórico: filtros continuam montados dinamicamente com `DynamicParameters` tipados, sem a expressão PostgreSQL problemática `@param is null`.
- PrintWizard: o bloco monolítico foi substituído pelo partial de conferência com quatro cards sem tabela, justificativa espaçosa e botões nativos com `type`, `formaction` e `formmethod`.
- Administração: KPIs alinhados à entrega e catálogo agrupado em Segurança e Acesso, GED e Operação, Etiquetas e Impressão e Sistema e Qualidade.
- Plano de Classificação: hero expandido, seção visual de cards e orientação quando não existem classes.
