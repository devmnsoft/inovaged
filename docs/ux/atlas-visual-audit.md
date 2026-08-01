# Auditoria visual Atlas — evolução 04.1.33

**Base auditada:** `1397c7491831b4ae163b7e5907864ccc4bf67222`

## Diagnóstico

O `AppIconTagHelper` transformava paths de origens distintas em SVGs exclusivamente lineares, aplicando `fill="none"` e `stroke="currentColor"` no contêiner. Paths fechados do catálogo, concebidos para preenchimento, ficavam vazios; classes do consumidor eram substituídas e nomes ausentes viravam um quadrado sem diagnóstico. As ilustrações possuíam variações de nomes “premium”, mas repetiam estrutura e não ofereciam contrato seguro de uso.

| Componente | Ícone atual | Fonte | Problema | Ilustração atual | Problema visual | Ação necessária | Status |
|---|---|---|---|---|---|---|---|
| App Shell | `app-icon` + Bootstrap Icons | catálogo de paths / fonte local | sistemas misturados e classes perdidas | — | hierarquia fragmentada | sprite Atlas e migração progressiva | Em evolução |
| Dashboard | Bootstrap Icons e SVG | fonte / paths | pesos heterogêneos | estados genéricos | cartões concorrentes | linguagem Atlas e painel operacional | Em evolução |
| GED | Bootstrap Icons + `app-icon` | misto | toolbars inconsistentes | `empty-folder*` | composição repetida | ícones por arquivo e workbench contínuo | Em evolução |
| Busca | Bootstrap Icons / imagens | misto | sem taxonomia | `empty-search*` | metáfora duplicada | cena exclusiva e resultados explicados | Em evolução |
| Preview | Bootstrap Icons | fonte local | estados pouco diferenciados | `document-preview-*` | mesma base visual | cinco cenas Atlas distintas | Em evolução |
| Upload | Bootstrap Icons | fonte local | status sem linguagem comum | `upload-*` | estados derivados | seis cenas específicas | Em evolução |
| Login | imagem SVG | asset local | identidade desconectada | `login-ged-workspace.svg` | hub pouco expressivo | reconstruir hub 2.5D | Em evolução |
| Feedback | Bootstrap Icons | fonte local | ícone genérico por modal | — | pouca semântica | Atlas por natureza da ação | Em evolução |
| Administração | Bootstrap Icons | fonte local | inexistência de inventário | — | manutenção difícil | catálogo pesquisável protegido | Planejado |

## Inventário e regras de migração

- Atlas é o sistema principal nas superfícies migradas; Bootstrap Icons permanece somente como compatibilidade legada.
- Cada símbolo declara sua própria pintura (`fill` ou `stroke`) e possui `viewBox`.
- Nomes desconhecidos geram warning e fallback semântico, nunca quadrado silencioso.
- Assets informativos recebem texto alternativo; estados abaixo da dobra usam carregamento lazy e decoding assíncrono.
- A migração evita alterar motores existentes de busca, upload, preview, favoritos e notificações: a camada Atlas integra-se aos contratos funcionais atuais.
