# Relatório — Evolução 04.1.37

## Entrega

- SHA inicial: `7d3ad883c7f33947b910311e84fd51668ecee54e`.
- Branch: `codex/evolucao-04-1-37-atlas-premium-shell`.
- CSS: propriedade consolidada em oito camadas Atlas, sem manter as antigas definições concorrentes.
- Tokens: `--ig-page`, superfícies, texto, primários, accent e borda são a fonte única dos novos componentes.
- Shell: sidebar 280/76 px, topbar 68 px, context header compacto, canvas com quatro larguras e raiz única de overlays.
- Estruturas: page section, panel e command bar disponíveis para migração progressiva das telas.
- Segurança: o catálogo autorizado da command palette e sua construção por DOM foram preservados; não foram adicionadas URLs ou comandos fixos.
- Responsividade: paddings 24/18/12 px, drawers fullscreen no mobile e proteção contra scroll horizontal global.

## Homologação e riscos

A auditoria registra Login, Dashboard, navegação, GED, busca, uploads, protocolos, empréstimos, usuários, administração e mobile. Estados dependentes de tenant/dados não foram fabricados. Aprovação visual humana e capturas autenticadas continuam pendentes no ambiente da equipe, por isso a PR permanece draft.

## Rollback

Reverter os commits desta evolução restaura atomicamente os arquivos CSS anteriores, o markup do layout e os componentes estruturais. Não há migração de banco ou alteração de contrato persistido.
