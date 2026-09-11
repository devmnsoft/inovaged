# Auditoria de baseline RC32

| Área | Estado | Evidência | Ação RC32 |
|---|---|---|---|
| Falhas de upload | FUNCIONA | `FAILURE_STATUSES`, normalização, mensagem persistente, detalhe, retry e `correlationId` estão no cliente RC31.1. | Preservar. |
| Upload unitário/lote/chunk | FUNCIONA | Os fluxos convergem para os serviços atuais e mantêm metadados no fallback. | Não duplicar infraestrutura. |
| Classificação no upload | BUG | `ClassificationOptions` consulta tipos documentais, pesquisa apenas `Name` e devolve código/descrição artificiais vazios. | Consultar o plano tenant-scoped e pesquisar código, nome e descrição reais. |
| Seletor de classificação | PARCIAL | Campo usa `datalist`; guarda o GUID fora do texto, mas não apresenta resultado rico nem navegação completa. | Substituir por combobox acessível. |
| Metadados do lote | PARCIAL | Classificação e opções de processamento chegam ao backend; OCR/preview estão desativados no cliente batch. | Organizar em etapa Preparar e explicitar estados. |
| Override por arquivo | PENDENTE | A fila não possui editor recolhido de nome, notas, tipo e classificação por item. | Adicionar personalização compacta e precedência individual. |
| Resultado pós-upload | PARCIAL | Resumo, erro persistente e links Abrir/Classificar/Etiqueta existem. | Exibir processamento real, seleção e ações em lote. |
| Sugestão de classificação | FUNCIONA/PARCIAL | Há `GedClassificationSuggestionService`, controller e texto OCR; não está integrado ao resultado do workbench. | Reutilizar o fluxo existente após OCR, sempre com confirmação. |
| Temporalidade | FUNCIONA | `IDocumentCommands.ApplyClassificationAsync` é o caminho oficial e recalcula retenção. | Reutilizar em ações individuais e múltiplas. |
| Histórico de upload | FUNCIONA/PARCIAL | `upload_batch`/`upload_batch_item`, serviço, métricas e diagnóstico já existem. | Expor central e retry sem prometer recuperar `File` local. |
| Label batch | FUNCIONA/PARCIAL | Print wizard e batch recebem `SubjectIds`; o pós-upload abre apenas um documento. | Encaminhar somente sucessos selecionados ao planejamento existente. |
| New do Label Studio | BUG | Todos os `fieldset` são exibidos simultaneamente. | Implementar stepper de cinco etapas com validação por etapa. |
| Starter | FUNCIONA/PARCIAL | Serviço oficial cria o layout, porém não há preview read-only no assistente. | Adicionar `StarterPreview` usando starter + renderer. |
| Galeria | BUG | Cards abrem o endpoint de Preview em iframes 16:9. | Usar Thumbnail, proporção real e carregamento por `IntersectionObserver`. |
| Thumbnail | FUNCIONA | Endpoint dedicado renderiza sem evento/trace. | Consumir na galeria; manter read-only. |
| Preview real | PENDENTE | Live preview usa somente perfil de exemplo/branding. | Criar busca tenant-scoped e render read-only com design não salvo. |
| Concorrência | PENDENTE | Save atual atualiza o draft sem token esperado. | Adicionar `ExpectedUpdatedAt`, update condicional e 409 `DESIGN_CONFLICT`. |
| Blocos pessoais | PENDENTE | Não foi encontrada estrutura equivalente completa. | Criar preset tenant-scoped, archive e regeneração de IDs. |
| Import/export | PENDENTE | Não há pacote de template canvas versionado e validado. | Implementar import como novo draft e rejeitar conteúdo inseguro. |
| Diff | PARCIAL | `ILabelCanvasDiffService` existe; UI de publicação não exibe o resumo semântico. | Reutilizar no modal de publicação e na seleção do elemento. |
| LocDesk/HOL | FUNCIONA | Contextos legados e modelos padrão permanecem separados. | Preservar compatibilidade. |
| Quality gates | PARCIAL | Doctors RC31.1 existem; gates RC32 não existem. | Adicionar checks de intake e conclusão do studio. |
