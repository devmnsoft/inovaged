# Análise de regressão do layout — Evolução 04.1.27-R1

## Referências e método

A base funcional registrada antes da branch foi `399f0106cd0de383b2563dfe5deb7668377d27c7`. A identidade aprovada foi inspecionada em `0e0de41249d44c41a9c4d0e735ea9f3e758968e7`; também foram comparados `29d2bc95`, `0aac61be`, `98ec7383` e `3591fd7`. Os worktrees `../inovaged-layout-aprovado` e `../inovaged-layout-atual` preservam as duas primeiras referências.

## Diagnóstico

| Área | Sintoma | Causa técnica / arquivo | Impacto | Correção | Evidência |
|---|---|---|---|---|---|
| Shell | Sidebar e conteúdo sem contrato flex completo | `design-system/layout.css` declarava dimensões parciais enquanto `inovaged.layout.css` tratava o documento | risco de sobreposição e largura ociosa | propriedade estrutural única em `inovaged.shell.css` | contrato automatizado de `.app-shell` e `.app-main` |
| Sidebar | superfície marinho, ativo translúcido e indicador fino | `design-system/navigation.css` e tema concorriam | identidade distinta da aprovada | superfície branca, borda clara e ativo azul sólido | `PremiumUiContractTests` |
| Topbar | transparência e blur | `design-system/navigation.css` | ruído visual | superfície branca opaca e altura de 60 px | CSS canônico |
| CSS | 15 folhas globais e tokens duplicados | `_Layout.cshtml`, `design-system/*`, tema e `inovaged.*` | cascade imprevisível | quatro camadas canônicas + utilities | `analyze-css-contracts.py` |
| Administração | CSS de página em todas as rotas | `_Layout.cshtml` | vazamento de regras | `@section Styles` somente na página | contrato de layout |
| Login | painel quase preto, orbes, glass e excesso de conteúdo | `pages/login.css` | divergência institucional e baixa simplicidade | painel azul, detalhe azul-verde e formulário branco | contrato CSS e revisão do markup |

## Comparação funcional

A versão aprovada tinha um shell simples (`wrapper/sidebar/main`), topbar clara e navegação familiar. A versão atual preservava rotas e permissões, mas espalhava responsabilidades entre reset, layout, navigation, theme e arquivos legados. A restauração mantém o menu e as regras de negócio atuais, recuperando o vocabulário visual aprovado sem copiar controllers ou serviços antigos.

Dashboard, GED e Administração herdam novamente cards, tabelas, formulários, densidade e shell comuns. O GED mantém suas folhas operacionais existentes nesta primeira restauração; sua consolidação completa exige homologação renderizada para evitar regressão funcional.
