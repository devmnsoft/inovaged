# Evolução 04.1.35 — Atlas Operational Workspace

- **SHA inicial:** `dab171361ccda6de6422630b3c7e4550c09700d6`
- **Branch:** `codex/evolucao-04-1-35-atlas-operational-workspace`
- **PR:** draft; sem merge automático.
- **Estratégia:** evolução progressiva do Atlas, da Busca Global e do `GedBulkUpload` existentes, sem mecanismos paralelos.

## Fluxos, cliques e navegação

Foram inventariados entrar, localizar documento, abrir pasta, enviar e acompanhar upload, solicitar OCR, classificar, editar metadados, mover documento, criar protocolo, solicitar documento, atender empréstimo, consultar histórico, abrir notificação, usar Busca Inteligente e usar Assistente. A matriz de páginas, modais, recargas, dúvidas e melhorias está em `operational-workspace-flow-audit.md`.

Metas alcançadas pela camada entregue: abrir documento em até duas ações, abrir Minha Fila em uma ação, iniciar envio no GED pelo teclado e criar protocolo diretamente pela busca operacional. Comandos cuja implementação funcional não estiver presente no DOM ou no módulo carregado são omitidos, evitando ações cenográficas.

## Entregue

### Sidebar, topbar, favoritos e recentes

A navegação autorizada e orientada por grupos existente foi preservada. Esta entrega não substitui os serviços de preferências, favoritos ou recentes e não fabrica itens. A topbar continua usando a Busca Global e o menu Criar existentes.

### Command Palette e atalhos

A Busca Global agora inclui o grupo **Ações**, reaproveita o painel, os resultados remotos e o atalho `Ctrl/Command+K` existentes. Os comandos levam ao GED, protocolo e Minha Fila; upload, notificações e Assistente só aparecem quando sua implementação funcional está disponível. `Ctrl+Shift+U` aciona o upload existente; `Ctrl+F`, `Enter`, `Space`, `Delete` e `Esc` respeitam campos editáveis e emitem eventos contextuais para os módulos da página.

### GED, upload e ações recuperáveis

O workspace contínuo, preview, drag and drop, filtros, seleção e `GedBulkUpload` da baseline foram preservados. Nenhum segundo motor foi criado. A API progressiva `WorkspaceUndo.offer` fornece mensagem, ação **Desfazer**, expiração configurável e callback real do chamador; ela não é anexada automaticamente a ações irreversíveis.

### Feedback, onboarding, mobile e conectividade

Foi adicionada orientação curta, dispensável e persistida somente como preferência local. O aviso offline reage a `online`/`offline`, não promete sincronização e não apaga formulários. As superfícies têm adaptação mobile, foco visível, regiões semânticas e respeitam o shell Atlas claro.

## Preservado sem regressão intencional

Atlas registries e tag helpers, sprite, resolver visual de arquivos, buscas Global e Inteligente, chunk upload, retry, pausa, retomada, drag and drop, preferências, multi-tenant, auditoria e permissões não tiveram seus motores alterados.

## Validação

- `node --check` foi aplicado a todos os JavaScripts em `wwwroot/js`.
- A busca estática não encontrou `window.alert`, `window.confirm`, `eval`, `new Function` ou `document.write` nos módulos novos.
- O SDK .NET não está instalado no container; clean, restore, builds Debug/Release, publish e execução local não puderam ser realizados.
- Conforme a proibição da tarefa, nenhum projeto de teste, workflow, gate, snapshot ou golden foi alterado e `dotnet test` não foi executado.

## Funcionalidades não concluídas

Dashboard operacional e `IUserWorkQueueService`; favoritos/recentes com reordenação; visões e tabela configurável; edição em lote persistida; integração do callback de desfazer com endpoints; abas adicionais de preview, timeline, versões e relacionados; evolução de Protocolos/Empréstimos; centro persistido de atividades/notificações; ações confirmadas do Assistente; rascunhos locais com análise de sensibilidade; homologação mobile autenticada. A baseline contém partes dessas áreas, mas elas não foram declaradas concluídas sem execução e evidência.

## Riscos restantes

1. As rotas exigem tenant, banco, identidade e permissões reais; a inspeção estática não substitui homologação.
2. Atalhos de exclusão apenas emitem evento e exigem que a página autorizada apresente sua confirmação real.
3. O callback de desfazer deve ser transacional e auditado pelo fluxo integrador.
4. Sem SDK e navegador autenticado não há evidência de HTTP 500, asset 404, console limpo ou compatibilidade visual renderizada.

Por esses riscos e itens pendentes, a PR deve permanecer **draft**.

## Rollback

Reverter os commits desta branch na ordem inversa. O retorno integral é o SHA `dab171361ccda6de6422630b3c7e4550c09700d6`; não há migração de banco ou alteração de dados para desfazer.

## Checklist de conclusão

- [x] Auditoria e contagem explícita dos fluxos prioritários.
- [x] Command palette integrada à Busca Global existente.
- [x] Comandos indisponíveis omitidos em vez de simulados.
- [x] Atalhos sem captura indevida em campos editáveis.
- [x] Superfície configurável de desfazer para ações reversíveis.
- [x] Onboarding curto e não obrigatório.
- [x] Estado de conectividade responsivo.
- [x] Node check.
- [x] Nenhum teste ou CI alterado; `dotnet test` não executado.
- [ ] Dashboard, fila e dados operacionais homologados.
- [ ] GED completo, protocolos, empréstimos e Assistente homologados.
- [ ] Build Debug e Release.
- [ ] Publish Release.
- [ ] Rotas, console e captura visual autenticada.
