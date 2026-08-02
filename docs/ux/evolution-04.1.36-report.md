# Relatório — evolução 04.1.36 Atlas Adaptive Workspace

## Identificação

- **SHA inicial:** `253f00944e9fac9c28812aafd7820572a1c43500`
- **Branch:** `codex/evolucao-04-1-36-atlas-adaptive-workspace`
- **Pull Request:** draft; referência registrada pela automação de PR após o commit final.

## Entregue neste incremento

### Fluxos auditados

A matriz em `adaptive-workspace-flow-audit.md` documenta os 17 fluxos solicitados, baseline observável, perda de contexto e objetivo de redução de ações. A execução autenticada não foi simulada sem credenciais ou banco.

### Command Catalog

- Contratos independentes de UI em `InovaGed.Application/Workspace/Commands`.
- Catálogo server-side registrado no container, com contexto de tenant, usuário, roles, módulo, controller, action e pasta.
- `GET /Workspace/Commands` retorna somente JSON agrupado e recusa identidades sem tenant/usuário válidos.
- URLs de navegação são produzidas por `LinkGenerator`; o cliente não contém rotas de negócio.
- Recursos dependentes de contexto, como upload, só são retornados no GED com pasta atual.
- A criação de protocolo exige módulo habilitado e papel compatível.

### Command Palette

- Removido o array fixo de comandos, as rotas fixas, Bootstrap Icons e `innerHTML` do módulo.
- Atlas Icons são construídos com DOM seguro e `<use href="#atlas-icon-*">`.
- A abertura inicial consulta comandos autorizados, portanto não exibe uma tela vazia.
- Há estados de loading, vazio e erro, debounce, cancelamento de requisição, grupos, atalhos e delegação de eventos.
- O endpoint é injetado pelo Razor via `Url.Action`, sem URL de API fixa no JavaScript.

## Validação

| Verificação | Resultado |
|---|---|
| `node --check InovaGed.Web/wwwroot/js/workspace-command-palette.js` | aprovado |
| `git diff --check` | aprovado |
| build Debug por fase | bloqueado: SDK `dotnet` ausente no container |
| build Release | bloqueado: SDK `dotnet` ausente no container |
| publish | bloqueado: SDK `dotnet` ausente no container |
| console / rotas autenticadas | não executado: aplicação não pode ser compilada/iniciada sem SDK |

Nenhum teste foi criado, alterado ou executado, conforme o escopo.

## Funcionalidades não concluídas

Este incremento **não declara concluídas** as fases 2–12: shell adaptativo completo, persistência e central de preferências, dashboard modular, catálogo de widgets e presets, fila com concorrência ao assumir, Inspector universal, estado completo do GED, visões compartilháveis, favoritos/recents persistidos, continuar trabalho, atividades, notificações acionáveis, Busca Inteligente contextual em todas as superfícies, rascunhos confirmáveis do Assistente, desfazer operacional, onboarding por recurso, ajuda, atalhos sequenciais e adaptação mobile. A frequência de comandos no backend também permanece pendente.

A PR deve permanecer **draft**. Declarar essas superfícies prontas sem implementação, dados reais e homologação autenticada produziria painéis cenográficos e contrariaria os critérios da evolução.

## Riscos restantes

1. O catálogo inicial cobre os comandos operacionais comprovados pela baseline, mas precisa ser ampliado junto aos serviços reais de favoritos, recentes e visões.
2. As regras por role são conservadoras; policies específicas devem substituir ou complementar papéis conforme o mapa de autorização do produto.
3. A integração usa a Busca Global existente para evitar um mecanismo duplicado; a partial modal legada deve ser removida apenas após confirmar que nenhuma tela a instancia.
4. Build e homologação precisam ocorrer em ambiente com .NET 8, banco migrado e usuário de cada perfil.

## Rollback

Reverter, na ordem inversa, os commits desta branch. O incremento não cria migration nem altera schema, portanto o rollback não exige operação no banco.

## Checklist de conclusão global

- [x] Auditoria documentada
- [x] Catálogo central e endpoint tenant-scoped
- [x] URLs removidas do JavaScript
- [x] Atlas Icons via DOM seguro
- [x] Loading, vazio e erro na paleta
- [ ] Frequência persistida e grupos de favoritos/recentes
- [ ] Preferências completas no backend
- [ ] Dashboard, widgets e presets
- [ ] Fila operacional e conflito ao assumir
- [ ] Inspector e GED contextual completos
- [ ] Visões, favoritos e recentes completos
- [ ] Atividades e notificações completas
- [ ] Busca e Assistente contextuais completos
- [ ] Desfazer, onboarding, ajuda e atalhos completos
- [ ] Mobile adaptativo
- [ ] Build Debug e Release
- [ ] Publish e homologação sem erros de console
