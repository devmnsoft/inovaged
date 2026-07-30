# Diagnóstico emergencial do template — 04.1.30-R1

Baseline analisada: `c8ee333b4818af9a8ed34a4b0eb5b6e83a34190c`. Referência: `0e0de41249d44c41a9c4d0e735ea9f3e758968e7`.

| Sintoma | Causa | Arquivo | Componente | Impacto | Correção | Evidência |
|---|---|---|---|---|---|---|
| Título da página ausente | breadcrumb substituiu título/subtítulo | `_Layout.cshtml` | topbar | contexto da página ficou ambíguo | topbar tipada usa `PageTitle` e `PageSubtitle` | `TopbarRecoveryTests` |
| Shell difícil de manter | claims, roles, setor, iniciais e UI no layout | `_Layout.cshtml` | shell | alto acoplamento e regressões | cálculo movido para `UserShellContextService` | layout reduzido a menos de 170 linhas |
| Navegação imprevisível | seções em `details`/`summary` e recolhimento | `_SidebarMenu.cshtml` | sidebar | itens ocultos e estado variável | menu plano por perfil | `SidebarRecoveryTests` |
| Ações cenográficas | paleta, sino e assistente sem fluxo completo | layout e partials de AppShell | topbar/drawers | promessa funcional falsa | partials preservados, mas não renderizados; flags desligadas | contrato de layout |
| CSS estrutural opaco | regras minificadas e múltiplos conceitos no mesmo arquivo | `inovaged.shell.css` | shell | conflitos difíceis de diagnosticar | CSS legível em blocos e proporções históricas | teste de seletores estruturais |
| Feedback duplicado | modal no layout e camada de experiência separada | `_Layout.cshtml`, `_AppExperienceLayers` | feedback | risco de IDs duplicados | partials únicos de toast e confirmação | `FeedbackRecoveryTests` |
| Mobile dependente de navegação | offcanvas não fechava explicitamente ao escolher item | `app-shell.js` | navegação mobile | painel poderia permanecer aberto | fecha ao clicar e devolve foco ao acionador | `MobileNavigationRecoveryTests` |
| Requisição sem elemento visível | badge carregava sempre e engolia erro | script inline do layout | classificação | tráfego e falha silenciosa | requisição removida do shell | ausência do script no layout |

Não foi identificada necessidade de alterar regras de negócio ou migrations.
