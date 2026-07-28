# Relatório final — Evolução 04.1.22

- **SHA inicial:** `a7e0dbce058224113509f590ad4add464d898dd4`
- **Branch:** `codex/evolucao-04-1-22-restaurar-layout-original`
- **Referência visual:** `0e0de41249d44c41a9c4d0e735ea9f3e758968e7`
- **Tema:** `inovaged-classic`, azul `#2563eb`, verde `#22c55e`.
- **Causa:** design system e legado carregados juntos, tokens marinho e navegação plana excessiva.
- **Sidebar:** seis grupos, 260/72 px, grupo ativo expandido, destaque azul com filete verde.
- **Topbar:** altura canônica de 62 px; fluxo funcional preservado.
- **Componentes:** cards, botões, tabelas, campos e foco normalizados no tema.
- **Backend/rotas/migrations:** não alterados.
- **Acessibilidade:** skip link, landmarks e labels existentes preservados; foco e redução de movimento reforçados.
- **Rollback:** reverter o commit desta evolução restaura integralmente os assets anteriores, sem rollback de banco.

## Pendências e riscos

Screenshots autenticados, Playwright, comparação pixel a pixel e CI remoto dependem do ambiente de homologação. A PR permanece draft. O CSS legado ainda existe no repositório para páginas independentes, mas não é carregado pelos layouts padrão.
