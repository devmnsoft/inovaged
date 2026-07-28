# Relatório da evolução 04.1.19

- SHA inicial: `c6e6e9b789f5ab967e11d3b20755916033a75f88`.
- Branch: `codex/evolucao-04-1-19-restauracao-template-premium`.
- Segurança: segredo removido da configuração ativa, exemplo seguro e arquivos locais ignorados; guard de CI adicionado.
- Razor: partial de badges tipada; Administração sem collection expressions; compilação no build/publish habilitada.
- Continuidade: migration já encadeada pelo orquestrador; asserção dedicada e fallback amigável adicionados.
- UX: design system em camadas, shell componentizada, navegação persistente, login externalizado e SVG local tipado.
- Homologação: build, publish, Playwright, acessibilidade e screenshots não executados porque `dotnet` não está instalado no contêiner. A PR deve permanecer draft.
- Rollback: reverter o commit desta evolução; nenhuma migration destrutiva foi criada.
