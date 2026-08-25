# Atlas UI Design System

## Objetivo
Atlas UI é a camada visual interna do InovaGED. Ela oferece uma linguagem premium, acessível e estável sem alterar regras de negócio. Os tokens e componentes globais vivem em `wwwroot/css/atlas-ui.css`; classes legadas `ig-*` continuam compatíveis durante a migração.

## Tokens visuais
Os tokens `--atlas-primary`, `--atlas-accent`, superfícies, bordas, texto, estados, raios e sombras devem ser usados em vez de valores locais. As cores de estado são semânticas: sucesso, atenção, perigo, informação, neutro e implantação.

## Componentes disponíveis
Os partials ficam em `Views/Shared/Atlas`: `AtlasPageHero`, `AtlasKpiCard`, `AtlasActionCard`, `AtlasStatusBadge`, `AtlasAlert`, `AtlasEmptyState`, `AtlasToolbar`, `AtlasTableShell`, `AtlasStepper`, `AtlasFormSection` e `AtlasPreviewShell`. São deliberadamente pequenos e aceitam `ViewData`.

## Uso dos componentes
- **PageHero:** informe `Title`, `Subtitle`, `Icon` e, opcionalmente, conteúdo `Actions`.
- **KpiCard:** informe `Label`, `Value` e `Hint`.
- **ActionCard:** informe `Icon`, `Title`, `Description`, `Badge`, `Url` e `Disabled`.
- **StatusBadge:** informe `Label` e `Tone` (`success`, `warning`, `danger`, `info`, `muted` ou `implementation`).
- **Alert:** informe `Tone`, `Icon`, `Title`, `Message` e `Recommendation`. Tons adicionais: `schema`, `migration`, `print` e `incident`.
- **EmptyState:** informe `Icon`, `Title` e `Message`; valores seguros são usados por padrão.
- **TableShell:** informe título/descrição e componha uma `table atlas-table` responsiva.
- **Stepper:** passe `IReadOnlyList<string>` em `Steps` e a etapa em `Current`.
- **FormSection:** agrupe campos relacionados com título e descrição.
- **PreviewShell:** encapsule a prévia mantendo as regras específicas de impressão fora do componente.

## Boas práticas Razor
Declare coleções em bloco `@{ }` antes de iterar. Não crie tuplas inline em `@foreach`. Não use `@media` em `.cshtml`: mantenha responsividade no CSS externo. Use `app-icon` para aliases e fallback. Preserve uma única ação primária por contexto.

## Validação
Execute `dotnet run --project InovaGed.Environment.Doctor -- ui-consistency`. O comando analisa Razor, tabelas, cards, heroes, views de etiquetas e ícones, e grava relatórios Markdown e JSON em `artifacts/ui`.
