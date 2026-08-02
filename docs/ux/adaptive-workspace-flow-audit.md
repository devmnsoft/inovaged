# Evolução 04.1.36 — auditoria do workspace adaptativo

**Baseline:** `253f00944e9fac9c28812aafd7820572a1c43500`  
**Método:** inspeção das rotas, controllers, views e módulos JavaScript da baseline. A homologação autenticada depende de banco e credenciais do ambiente e, por isso, não é representada como executada neste documento.

| Fluxo | Ações atuais | Páginas | Reloads | Ações escondidas / duplicações | Orientação e estado perdido | Melhoria aplicada | Ações finais |
|---|---:|---:|---:|---|---|---|---:|
| Login | 1 | 1 | 1 | — | retorno não contextual | preservar destino solicitado | 1 |
| Dashboard | 2 | 1 | 1 | atalhos dispersos | não retoma contexto | comandos frequentes, favoritos e recentes no catálogo | 1 |
| Minha Fila | 2 | 1 | 1 | entrada escondida na paleta | filtro depende da página | comando contextual autorizado | 1 |
| Buscar documento | 2 | 1 | 0 | busca e comandos dividem o painel | consulta perdida ao navegar | resultados e comandos no mesmo workspace | 2 |
| Abrir pasta | 2 | 1 | 1 | ações de pasta misturadas | seleção e scroll podem ser perdidos | contexto `folderId` enviado ao catálogo | 1 |
| Abrir documento | 2 | 1 | 1 | preview e detalhes separados | lista pode ser perdida | preparar abertura contextual por drawer | 1 |
| Editar metadados | 3 | 2 | 1 | ação depende do detalhe | retorno manual | comando somente no contexto autorizado | 2 |
| Enviar arquivos | 2 | 1 | 0 | atalho restrito ao GED | pasta nem sempre explícita | evento autorizado e contextual | 1 |
| Mover documento | 4 | 1 | 0 | ação em menus distintos | seleção pode ser perdida | ação contextual por seleção | 3 |
| Solicitar OCR | 3 | 1 | 0 | ação compete com ações de pasta | feedback disperso | comando contextual quando GED estiver ativo | 2 |
| Classificar documento | 3 | 2 | 1 | entrada pouco visível | retorno manual | catálogo considera módulo e perfil | 2 |
| Criar protocolo | 2 | 2 | 1 | URL estava fixa no cliente | contexto de origem perdido | URL gerada no servidor | 1 |
| Abrir empréstimo | 2 | 2 | 1 | acesso via navegação | lista perdida | comando de navegação autorizado | 1 |
| Notificação | 2 | 1 | 0 | drawer e página desconectados | item de origem não preservado | evento de drawer vindo do catálogo | 1 |
| Atividade | 2 | 1 | 0 | indicador disperso | operação de origem pouco clara | comando de drawer contextual | 1 |
| Command Palette | 1 | 0 | 0 | comandos e URLs hardcoded, Bootstrap Icons | sem loading/erro/histórico | endpoint tenant-scoped, grupos e Atlas Icons | 1 |
| Assistente | 2 | 0 | 0 | acesso depende do shell | contexto implícito | evento autorizado conforme módulo | 1 |

## Achados verificáveis da baseline

1. `workspace-command-palette.js` continha um array fixo com rotas e ícones Bootstrap.
2. O módulo escrevia marcação com `innerHTML`, embora parte do conteúdo fosse corrigida depois com `textContent`.
3. Os comandos não eram obtidos de um catálogo central e não carregavam tenant, usuário, módulo ou pasta.
4. O painel de busca global já oferecia o ponto correto de integração; ele foi preservado para evitar um segundo mecanismo.

## Critério de contagem

“Ações” contabiliza decisões ou cliques necessários após a entrada no shell; “reload” contabiliza navegação completa. Os números finais são metas do fluxo implementado nesta evolução e devem ser reconfirmados na homologação autenticada.
