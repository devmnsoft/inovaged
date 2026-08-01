# Nexus Workspace — diagnóstico da base (04.1.32)

## Escopo auditado

A auditoria partiu do SHA `d5e296ef393b1b78b93ce2486dbb8748ef9dc176` e cobriu o layout compartilhado, App Shell, login, GED, dashboards, Busca Inteligente, documentos hospitalares, estilos, JavaScript e a migração `2026_07_workspace_productivity.sql`.

## Estado real encontrado

- O layout autenticado já é dividido em sidebar, topbar, cabeçalho contextual e conteúdo. Existem duas gerações de partials no diretório `AppShell`; `_Sidebar`, `_Topbar` e `_ContextHeader` são os consumidores atuais.
- A navegação possui catálogo autorizado, busca local, menu mobile, busca global e criação rápida. O recolhimento da sidebar é mantido apenas no navegador por `app-shell.js`.
- A migração de produtividade já cria preferências, favoritos, recentes, visões salvas, notificações e atividades com isolamento por `tenant_id` e `user_id`, mas a aplicação ainda não possui serviços que consumam essas tabelas.
- O GED preserva o upload em lote, seleção, ações em lote, drag and drop, navegação de pastas e preview. `Views/Ged/Index.cshtml` concentra árvore, command bar, listagem, preview e diálogos, tornando a evolução mais arriscada.
- O preview atual abre por seleção sem recarregar a página, porém ainda não oferece a navegação completa por informações, metadados, histórico, versões e relacionados solicitada para o Nexus.
- A Busca Inteligente possui pipeline e UI próprios. A busca global autorizada é separada e deve continuar usando os provedores existentes, sem um novo índice.
- Toasts e confirmação tipada já existem em `inovaged-feedback.js`; ainda há markup legado e chamadas pontuais que precisam convergir gradualmente para essa camada.

## Contratos de JavaScript preservados

Os módulos do GED consomem intensamente seletores `#gedExplorer`, `#gedGlobalDropOverlay`, `#gedDocumentPreview`, `.js-folder-node`, `.js-document-row`, atributos `data-folder-id`, `data-document-id`, `data-upload-folder-id`, `data-listing-folder-id` e eventos customizados do upload. Esses contratos não devem ser removidos durante a divisão dos partials.

## Lacunas funcionais confirmadas

1. Não há persistência server-side das dimensões do workbench ou do estado da sidebar.
2. Favoritos, recentes e visões salvas possuem schema, mas não estão integrados à experiência autenticada.
3. Atividades e notificações possuem schema, mas não há central de workspace conectada às tabelas.
4. A identidade visual já usa azul e verde, porém tokens, dimensões e profundidade ainda divergem da especificação Nexus.
5. O Dashboard não consolida uma fila acionável única por perfil.
6. O Assistente documental com fontes autorizadas não está implementado; portanto, nenhum acionador deve ser exibido até existir serviço e endpoint reais.

## Estratégia segura

A evolução deve manter IDs e atributos atuais, introduzir persistência de modo aditivo, extrair partials sem consultas e somente renderizar centrais quando seus serviços reais estiverem disponíveis. Recursos sem backend não devem receber dados demonstrativos nem botões cenográficos.
