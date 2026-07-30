# Arquitetura CSS

Ordem canônica: Bootstrap local, Bootstrap Icons local, tokens, base, shell, componentes, seção de página e utilities. `inovaged.shell.css` é o único proprietário de `.app-shell`, `.app-main`, `.app-sidebar` e `.app-topbar`. `scripts/ci/analyze-css-contracts.py` fiscaliza tokens, ownership estrutural, estilos inline estruturais nos layouts, cores de marca literais e excesso de `!important`.

Os onze arquivos de `design-system/`, `themes/inovaged-classic.css` e `inovaged.layout.css` foram consolidados. Folhas operacionais legadas não carregadas globalmente permanecem para migração incremental segura.
