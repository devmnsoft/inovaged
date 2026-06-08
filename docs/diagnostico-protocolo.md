# Diagnóstico técnico — Protocolo, Workflow e Loans

## O que existe no repositório

### Protocolo
- **Controller operacional:** `InovaGed.Web/Controller/ProtocoloController.cs` possui listagem, criação, detalhes, anexos, tramitação, finalização, encerramento e arquivamento.
- **Rotas principais existentes:** `/Protocolo`, `/Protocolo/Novo`, `/Protocolo/Details/{id}` e ações POST de anexar/tramitar/finalizar/encerrar/arquivar.
- **Rotas operacionais adicionadas/consolidadas:** `/ProtocolRequests` para solicitação/acompanhamento pelo ARQUIVISTAOPHIR e `/Protocols/WorkQueue` para fila/manuseio por ADMINISTRADOROPHIR.
- **Views:** `InovaGed.Web/Views/Protocolo/Index.cshtml`, `Details.cshtml` e `Novo.cshtml`; views complementares em `ProtocoloMelhorias`, `ProtocoloCadastros`, `ProtocoloParametros` e `ProtocoloUsuariosSetorAvancado`.
- **Modelos:** `InovaGed.Web/Models/Protocolo/ProtocoloViewModels.cs` e arquivos correlatos de melhorias/cadastros.
- **Tabelas identificadas no script base:** `ged.protocolo`, `ged.protocolo_documento`, `ged.protocolo_tramitacao`, `ged.protocolo_observacao`, `ged.protocolo_setor`, cadastros auxiliares e participantes.

### Workflow
- **Controllers:** `DocumentWorkflowController`, `GedWorkflowController` e `WorkflowController`.
- **Infraestrutura:** comandos/queries em `InovaGed.Infrastructure/Workflow/*` e repositório documental em `InovaGed.Infrastructure/Document/DocumentWorkflowRepository.cs`.
- **Views:** `InovaGed.Web/Views/Workflow/*`, `InovaGed.Web/Views/GedWorkflow/_WorkflowHistory.cshtml` e painel GED parcial.
- **Ajuste de UX realizado:** ciclo de vida e workflow saíram da visualização principal aberta e ficaram recolhidos na aba/área “Fluxo” da tela de detalhes do documento.

### Loans / Solicitações
- **Controller:** `InovaGed.Web/Controller/LoansController.cs` com listagem, criação, detalhes, vencidos e transições approve/deliver/return/cancel.
- **Serviços:** interfaces em `InovaGed.Application/Ged/Loans/*` e implementação em `InovaGed.Infrastructure/Ged/Loans/*`.
- **Views:** `InovaGed.Web/Views/Loans/Index.cshtml`, `New.cshtml`, `Details.cshtml`, `Overdue.cshtml` e `Profiles.cshtml`.
- **Solicitações adicionais:** `SolicitacoesController` e views em `InovaGed.Web/Views/Solicitacoes/*`.

## O que estava incompleto ou inconsistente

- A jornada de protocolo existia, mas não havia rotas explícitas separando **solicitação** (`ARQUIVISTAOPHIR`) e **fila/manuseio** (`ADMINISTRADOROPHIR`).
- O menu de perfis Ophir estava concentrado na busca hospitalar e não conduzia claramente para solicitar protocolo, minhas solicitações, fila de protocolos e aprovações de empréstimo.
- Loans listava/manuseava com visão global em todos os acessos do controller; agora a visão global fica limitada ao ADMIN/ADMINISTRADOROPHIR, enquanto ARQUIVISTAOPHIR cria e acompanha o que lhe pertence.
- Ciclo de vida e workflow apareciam abertos junto à visualização principal do documento, aumentando ruído operacional.

## Integrações necessárias e consolidadas

- **Perfil ADMIN:** mantém acesso completo e governança.
- **Perfil ARQUIVISTAOPHIR:** acessa `/ProtocolRequests`, cria protocolo em `/Protocolo/Novo`, acompanha solicitações e solicita empréstimo em `/Loans/New`.
- **Perfil ADMINISTRADOROPHIR:** acessa `/Protocols/WorkQueue`, manuseia processos e executa ações administrativas de Loans.
- **Controllers protegidos:** Protocolo e Loans agora aplicam restrição por roles em rotas administrativas; menu apenas complementa a segurança.
- **Auditoria:** Loans usa eventos operacionais específicos (`LOAN_REQUEST_CREATED`, `LOAN_APPROVED`, `LOAN_DELIVERED`, `LOAN_RETURNED` e rejeição/cancelamento). A classificação rápida registra `DOCUMENT_CLASSIFICATION_CHANGED`.

## Rotas consolidadas

| Jornada | Perfil principal | Rota |
|---|---|---|
| Buscar prontuários/documentos | Todos os perfis operacionais | `/HospitalDocuments` |
| Solicitar protocolo | ARQUIVISTAOPHIR / ADMIN | `/ProtocolRequests` e `/Protocolo/Novo` |
| Acompanhar solicitações | ARQUIVISTAOPHIR / ADMIN | `/ProtocolRequests` |
| Fila de protocolos | ADMINISTRADOROPHIR / ADMIN | `/Protocols/WorkQueue` |
| Manuseio de processos | ADMINISTRADOROPHIR / ADMIN | `/Protocols/WorkQueue` e detalhes do protocolo |
| Solicitar empréstimo | ARQUIVISTAOPHIR / ADMIN | `/Loans/New` |
| Aprovar/manusear Loans | ADMINISTRADOROPHIR / ADMIN | `/Loans` e detalhes |

## Próximos passos recomendados

1. Evoluir `ProtocoloController` para serviços de aplicação dedicados, reduzindo SQL inline no controller.
2. Criar testes automatizados de autorização por role para `/ProtocolRequests`, `/Protocols/WorkQueue`, `/Loans/*` e ações POST.
3. Padronizar eventos de auditoria do Protocolo em tabela/event writer comum, além da trilha `ged.protocolo_tramitacao`.
4. Adicionar filtros salvos e painéis de SLA/prioridade nas filas administrativas.
