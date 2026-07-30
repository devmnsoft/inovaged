# Evidências da restauração premium

| Tela | Aprovada | Atual | Restaurada | Problema corrigido | Observações |
|---|---|---|---|---|---|
| Shell | worktree `inovaged-layout-aprovado` | worktree `inovaged-layout-atual` | branch desta PR | flex, sidebar clara, topbar | screenshots pendentes de runtime autenticado |
| Login | commit `0e0de412` | SHA `399f0106` | markup/CSS canônicos | tema escuro e complexidade | screenshot pendente |
| Dashboard | commit `0e0de412` | SHA `399f0106` | herança canônica | cascade fragmentada | dados equivalentes pendentes |
| GED | commit `0e0de412` | SHA `399f0106` | shell restaurado | espaço principal instável | consolidação operacional pendente |
| Administração | commit `0e0de412` | SHA `399f0106` | CSS local por seção | vazamento global | renderização pendente |

## Estado honesto da evidência

Os diretórios `screenshots/approved`, `screenshots/current` e `screenshots/restored` foram reservados, mas imagens não são versionadas sem uma execução real com banco, tenant e usuário equivalentes. O container não possui o SDK `dotnet`; portanto a aplicação e Playwright não foram executados aqui. A PR deve permanecer draft e a Trilha B não pode começar até o gate visual real e a aprovação humana.
