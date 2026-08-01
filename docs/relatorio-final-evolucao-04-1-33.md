# Relatório — evolução 04.1.33 Atlas

## Entrega

- **SHA inicial:** `1397c7491831b4ae163b7e5907864ccc4bf67222`
- **Branch:** `codex/evolucao-04-1-33-atlas-visual-system`
- **Diagnóstico:** paths preenchidos e lineares eram submetidos aos mesmos atributos globais; classes eram descartadas e o fallback era um quadrado silencioso.
- **Sistema Atlas:** sprite inline único, registro tipado com aliases/categorias/variantes, Tag Helper acessível com tamanho/tom/variante e fallback com warning.
- **Catálogo:** `/Administration/AtlasIcons`, protegido pela política administrativa existente, inclui pesquisa e amostras 16/20/24/32.
- **Ilustrações:** contrato de paths seguros e 21 cenas locais registradas para login, pasta, busca, upload, preview e produtividade.
- **Marca e login:** símbolos proprietários azul-verde e login migrado integralmente para ícones/ilustração Atlas.
- **Fundação:** tokens, ícones, ilustrações, componentes e workspace responsivos, com transições discretas e `prefers-reduced-motion`.

## Compatibilidade e escopo restante

Bootstrap Icons permanece local para páginas legadas, conforme estratégia de migração progressiva. App Shell autenticado, dashboards por perfil, workbench GED completo, comparação de versões, metadados, relacionados, upload, busca, Assistente, produtividade e feedback preservam as implementações funcionais existentes, mas a migração visual de 100% dessas superfícies **não foi concluída nesta entrega**. Nenhum motor paralelo ou dado fictício foi criado.

## Validação e riscos

- O inventário assegura IDs únicos e cobertura integral do registro pelo sprite.
- SVGs Atlas possuem dimensões, `viewBox`, limite de tamanho e nenhum script, Base64 ou asset remoto.
- Os sete módulos JavaScript Atlas passam em análise sintática do Node.
- O SDK .NET não está instalado no ambiente, portanto Debug, Release, solução e publish permanecem pendentes e a PR deve continuar draft.
- Execução local, rotas autenticadas, console, assets 404 e captura visual dependem de runtime .NET e credenciais; permanecem pendentes.

## Rollback

Reverter os commits desta branch em ordem inversa restaura o catálogo anterior. O último ponto antes da evolução é `1397c7491831b4ae163b7e5907864ccc4bf67222`. Não há alteração de schema, dados, CI ou testes.

## Checklist

- [x] Sprite, registro, aliases e Tag Helper Atlas
- [x] Classes, tamanhos, tons e variantes preservados
- [x] Fallback Development/Production com warning
- [x] Catálogo administrativo pesquisável
- [x] Ícones preenchidos, lineares e por extensão
- [x] Ilustrações distintas e helper seguro
- [x] Marca e Login Atlas
- [x] Fundação CSS/JS e mobile base
- [ ] Migração completa das superfícies principais
- [ ] Build Debug/Release e publish (SDK ausente)
- [ ] Verificação manual autenticada, console e 404
