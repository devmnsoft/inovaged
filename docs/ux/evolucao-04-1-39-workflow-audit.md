# Evolução 04.1.39 — auditoria de workflow e dependências

## Identificação da base

- SHA recebido no checkout: `ffc5419d2796bbbcac5f0f0fef4fa85a8c024966`.
- Branch de trabalho: `codex/evolucao-04-1-39-collaboration-workflow-hub`.
- O checkout fornecido não contém a referência local `main` nem um remoto configurado. Por isso, não foi possível executar `git checkout main` ou `git pull --ff-only`; a branch foi criada a partir do HEAD de integração entregue pelo ambiente.

## Gate da evolução 04.1.38

A evolução 04.1.38 está integrada. A evidência funcional e de propriedade de CSS está registrada em `docs/ux/evolucao-04-1-38-auditoria.md` e foi conferida contra o código atual.

| Requisito | Evidência verificada | Resultado |
|---|---|---|
| Atlas Premium Shell e topbar global | `_Layout.cshtml`, `_Topbar.cshtml`, `atlas-shell.css` | Presente |
| Context Header tipado e breadcrumb contextual | `_ContextHeader.cshtml` e modelos compartilhados | Presente |
| Workspace Canvas com variantes | `_Layout.cshtml` aceita `standard`, `wide`, `full`, `workbench` e `focus` | Presente |
| Inspector universal e overlays | partials compartilhados e `atlas-overlays.css` | Presente |
| Modo Foco e Trilha de Trabalho | `app-shell.js` | Presente |
| GED em workbench | `Views/Ged/Index.cshtml` define a variante `workbench` | Presente |
| Templates premium | views e componentes compartilhados Atlas | Presente |
| CSS Atlas sem propriedade estrutural duplicada | matriz de propriedade da auditoria 04.1.38 | Presente |
| Folhas obrigatórias | `atlas-shell.css`, `atlas-navigation.css`, `atlas-command-palette.css`, `atlas-feedback.css`, `atlas-responsive.css` | Presentes |

## Gate inicial de compilação

Os comandos obrigatórios foram tentados antes de qualquer implementação funcional, mas a imagem de execução não contém o executável `dotnet` (`dotnet: command not found`, código 127). Assim, o gate inicial não pôde ser aprovado nem foi possível distinguir uma eventual falha da base de uma limitação do ambiente.

De acordo com a ordem obrigatória da evolução, a implementação funcional foi interrompida no fim da Fase 0. Nenhum schema, controller, serviço, JavaScript ou CSS colaborativo foi criado sem um build de base verificável.

## Motor de workflow existente

O repositório **já possui um motor de workflow documental**. A evolução 04.1.39 deve estendê-lo; criar um segundo motor seria duplicação arquitetural.

### Contratos e domínio

| Estrutura | Local | Capacidade atual | Direção para 04.1.39 |
|---|---|---|---|
| `IWorkflowQueries` / `IWorkflowCommands` | `InovaGed.Application/Workflow/WorkflowInterfacesAndCommands.cs` | Definições, etapas, transições e consultas tenant-aware | Evoluir os contratos com versionamento e publicação, sem renomear ou duplicar |
| `IDocumentWorkflowQueries` / `IDocumentWorkflowCommands` | `InovaGed.Application/Workflow/DocumentWorkflow.cs` | Inicia instância, lista estado/transições/histórico e aplica transição | Reutilizar como runtime; acrescentar decisão, etapa ativa, aprovadores e concorrência |
| Workflow de status documental | `InovaGed.Application/Documents/DocumentWorkflowService.cs` | Política de transições do ciclo documental e demandas de permissão | Preservar; integrar aprovações sem criar outra máquina de status |
| Tipos de domínio | `InovaGed.Domain/Workflow/` | DTOs/comandos e primitivas atuais | Tipar os novos estados e políticas no mesmo bounded context |

### Persistência e serviços

| Estrutura | Local | Capacidade atual | Lacuna comprovada |
|---|---|---|---|
| `WorkflowQueries` / `WorkflowCommands` | `InovaGed.Infrastructure/Workflow/` | Dapper com filtro por tenant para definição, etapa e transição | Definição publicada não é imutável/versionada; etapa ainda admite `required_role` textual |
| `DocumentWorkflowQueries` / `DocumentWorkflowCommands` | `InovaGed.Infrastructure/Workflow/` | Instância por documento, histórico e transições persistidas | Não modela aprovação sequencial/paralela, delegação, correção ou version token |
| Repositório documental | `InovaGed.Infrastructure/Document/DocumentWorkflowRepository.cs` | Persistência do ciclo de status do documento | Deve continuar sendo a fronteira do ciclo documental |
| Registro no DI | `InovaGed.Web/Program.cs` | Contratos e implementações de workflow registrados | Novas capacidades devem ser adicionadas ao registro existente |

### Controllers e interface

| Superfície | Local | Capacidade atual | Evolução segura |
|---|---|---|---|
| Administração | `InovaGed.Web/Controller/WorkflowController.cs`, `Views/Workflow/` | CRUD de definição, etapa e transição | Converter na administração versionada e aplicar policy `Workflow.Manage` |
| Runtime GED | `GedWorkflowController.cs`, `DocumentWorkflowController.cs` | Início, painel, histórico e transições de documento | Consolidar rotas sobre o mesmo runtime antes de expor aprovações |
| Inspector GED | `Views/Ged/_DocumentWorkflowPanel.cshtml`, `_WorkflowHistory.cshtml` | Estado e histórico no contexto do documento | Evoluir para a aba Processo somente quando houver instância |
| Protocolo | `ProtocolsController`, `ProtocolRequestsController` e `ProtocolDtos.cs` | Fila e estados de análise/ajuste/aprovação | Adaptar à Minha Caixa por projeção; não copiar registros |
| Empréstimos | `LoansController` e schema de perfis de aprovação | Decisão e aprovador no módulo de empréstimos | Projetar pendências autorizadas na caixa sem substituir o fluxo do módulo |

## Tabelas existentes a preservar

As consultas e migrations existentes referenciam `ged.workflow_definition`, `ged.workflow_stage`, `ged.workflow_transition`, `ged.document_workflow` e `ged.document_workflow_history`. Há índices tenant/documento para instâncias e histórico em `20260518_preview_timeline_loans_indexes.sql`.

A futura migration `2026_08_collaboration_workflow_hub.sql` deverá ser somente aditiva. Antes de criar `workflow_definition` ou `workflow_instance` com novos nomes, deve mapear essas tabelas existentes e acrescentar versionamento compatível. Instâncias ativas precisam manter uma referência imutável à versão publicada usada no início.

## Estruturas reutilizáveis fora do workflow

- `ProtocolWorkQueueVm` e os serviços de protocolo já fornecem uma fila tenant-aware que pode alimentar uma projeção de atenção.
- O fluxo de empréstimos já contém aprovador/status e índices para `tenant_id`, aprovador e status.
- A infraestrutura de notificações e SignalR existente deve ser ampliada por um cliente central, nunca por conexões por componente.
- Auditoria, `ICurrentUser`, fábricas de conexão e demandas de permissão existentes devem ser usadas como fronteiras obrigatórias.
- Preferências e visões salvas do workspace devem receber apenas novas categorias/chaves compatíveis.

## O que precisa evoluir

1. Criar uma projeção unificada `IWorkspaceInboxService`, sem SQL nos controllers e sem copiar dados dos módulos.
2. Acrescentar colaboração multi-tenant (threads, comentários, menções e acompanhamento) com autorização de contexto no backend.
3. Acrescentar tarefas/checklists e tomada condicional, usando token de concorrência.
4. Estender o runtime atual com definições versionadas, decisões e políticas de aprovação simples, sequencial e paralela.
5. Persistir anotações como camada por versão documental e coordenadas normalizadas.
6. Publicar eventos no SignalR e na timeline existentes, preservando autorização e auditoria.
7. Implementar automações por catálogos fechados de evento/condição/ação e simulação sem efeitos.

## O que não deve ser duplicado

- Motor, comandos, queries, controller ou tabelas de workflow já existentes.
- Máquina de estados do documento, filas de protocolo e aprovação de empréstimos.
- Conexão SignalR, sistema de notificações, auditoria, tenant context ou política de autorização.
- Shell, inspector, overlays, feedback, ícones, tokens e folhas estruturais Atlas.
- Calendário útil, quando localizado durante a fase de prazos.

## Riscos antes da implementação

1. O CRUD administrativo atual é protegido apenas por autenticação no controller; a policy de gestão deve anteceder a exposição do editor ampliado.
2. `required_role` textual não atende à exigência de policies; precisa de resolução autorizada de usuário/setor/perfil.
3. Definições atuais são alteráveis em lugar; publicar versões sem uma estratégia de compatibilidade pode mudar instâncias em andamento.
4. Há duas superfícies de controller para workflow GED; rotas e responsabilidades devem ser consolidadas antes de adicionar decisões.
5. Sem o SDK .NET, qualquer mudança funcional C#/Razor ficaria sem o gate mínimo exigido.

## Decisão da Fase 0

A 04.1.38 está presente e o motor existente foi identificado. Contudo, a Fase 1 permanece bloqueada pelo gate de compilação não executável na imagem atual. Esta entrega registra a dependência, impede a criação de um motor paralelo e mantém a Pull Request em draft. A retomada deve iniciar instalando/disponibilizando o SDK definido pelo repositório, repetindo clean/restore/build e somente então implementando as fases na ordem solicitada.
