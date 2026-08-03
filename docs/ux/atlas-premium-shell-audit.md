# Evolução 04.1.37 — auditoria visual do Atlas Premium Shell

**Base inspecionada:** `7d3ad883c7f33947b910311e84fd51668ecee54e`
**Método:** execução local e inspeção das superfícies renderizadas em desktop e viewport responsiva; o ambiente não contém dados produtivos, portanto estados dependentes de conteúdo foram verificados com os dados disponíveis.

## Matriz de auditoria

| Superfície | Problema visual/hierárquico observado | Componente/ícone/cor/espaço | Concorrência, cards e mobile | Correção aplicada |
|---|---|---|---|---|
| Login | formulário parecia isolado da linguagem do produto | proporção e superfície pouco institucionais | respiro inconsistente em viewport estreita | preservado template dedicado e consolidada a base tipográfica/tokens |
| Dashboard | regiões competiam como cards equivalentes | profundidade e espaçamentos divergentes | composição pouco adaptativa | canvas de 1600–1680 px e seções estruturais contínuas |
| Sidebar expandida | navegação genérica, ativo pesado | medidas 260 px, gradiente e cores literais | grupos tinham o mesmo peso | largura 280 px, ativo Atlas, marcador verde e foco visível |
| Sidebar recolhida | largura e alinhamento inconsistentes | textos dependiam de compressão | risco de sobreposição | largura 76 px, ocultação sem compressão e ícones centralizados |
| Topbar | hierarquia fraca com conteúdo | altura abaixo da especificação | busca perdia prioridade em tablet | altura 68 px e contexto integrado |
| Context Header | título repetia o contexto da topbar | cabeçalho funcionava como bloco isolado | ação concorria em mobile | faixa contínua de 84–112 px e canvas imediatamente abaixo |
| Command Palette | painel estreito e aparência de busca comum | catálogo visual misturado | estados vazios/loading pouco destacados | overlay consolidado, catálogo autorizado mantido e DOM seguro preservado |
| GED / Preview | painéis pareciam cards separados | gaps, bordas e sombras duplicavam limites | workbench apertava em tablet | variante full/workbench e divisores contínuos |
| Busca Inteligente | experiência lembrava formulário | facetas e resultados sem hierarquia | filtros ocupavam largura excessiva | variante wide e primitivas de painel/command bar |
| Upload Center | drawer concorria com overlays globais | progresso e feedback sem raiz comum | drawer estreito no mobile | overlay root único e drawer fullscreen responsivo |
| Protocolos / Empréstimos | tabelas administrativas genéricas | toolbars e densidades divergentes | ações comprimiam colunas | template wide, command bar e painéis estruturais |
| Usuários / Administração | excesso de caixas equivalentes | configurações sem fluxo visual | navegação interna quebrava cedo | canvas de configuração e seções com divisores |
| Feedback / onboarding / undo / conectividade | camadas apareciam desconectadas | estilos viviam em arquivos concorrentes | posição móvel inconsistente | raiz global única e camada `atlas-feedback.css` |
| Mobile 390 px | risco de scroll horizontal global | padding e drawers divergentes | ações e contexto competiam | padding 12 px, drawers fullscreen e overflow global bloqueado |

## Consolidação

A propriedade visual foi organizada em tokens, base, shell, navegação, componentes, overlays, feedback e responsividade. Os arquivos antigos de shell/workspace/componentes foram removidos para evitar cascata duplicada. Valores de identidade permanecem definidos apenas em tokens; componentes novos consomem `var(--ig-*)`.

## Limites da homologação

Estados que exigem tenant, permissões ou documentos reais não foram simulados. Nenhum botão, badge ou painel cenográfico foi adicionado. A aprovação visual humana continua sendo o gate final da PR draft.
