# Atlas UI Expansion — Módulos Operacionais

## Escopo revisado

Foram revisadas as rotas `/`, `/Home`, `/Dashboard`, `/Documents`, `/Search`, `/SmartSearch`, `/Physical/Boxes`, `/Retention`, `/RetentionDestination`, `/RetentionCase`, `/Instruments/Versions/PCD`, `/Instruments/Versions/TTD`, `/Instruments/Versions/POP`, `/SystemIncidents`, `/SystemIncidents/RouteHealth`, `/DatabaseReadiness`, `/SchemaHealth`, `/ReleaseReadiness`, `/UatReadiness`, `/Loans`, `/DocumentQuality` e `/Protocols/WorkQueue`.

`/PostGoLive`: **rota não encontrada**. Nenhuma rota artificial foi criada. `/ReleaseReadiness` existe e encaminha para o painel de prontidão da Administração. `/SchemaHealth` é uma central gerada pelo controller e não possui view Razor própria. As rotas `/Documents`, `/Search` e `/Dashboard` são atendidas pelos fluxos GED equivalentes do projeto.

## Views e melhorias

- Dashboard GED: hero executivo, indicadores e tabelas Atlas para atividades e pendências.
- Busca e SmartSearch: contexto de pesquisa, filtros preservados e apresentação Atlas.
- Acervo físico: hero, métricas, toolbar e tabela de caixas padronizados.
- Retenção, destinação e casos: contexto arquivístico, cards e listagens premium.
- PCD/TTD e POP: histórico de versões e publicação com superfície Atlas.
- Incidentes e Route Health: central técnica com hero, indicadores e tabelas.
- Database Readiness e UAT: governança técnica e de homologação responsiva.
- Empréstimos, qualidade e protocolos: KPIs, filtros e filas operacionais uniformes.

## Componentes, CSS e ícones

As views reutilizam `_AtlasPageHero`, as superfícies/tabelas `atlas-card` e `atlas-table` e os componentes Atlas já consolidados. A composição responsiva operacional foi acrescentada a `wwwroot/css/atlas-ui.css`. Foram reutilizados aliases seguros do registro (`dashboard`, `file-text`, `sparkles`, `box`, `map-pin`, `qr-code`, `alert-triangle`, `rocket`, `clipboard-check` e `scan-line`).

## QA visual

`tools/visual-qa/routes.json` inclui as rotas operacionais encontradas. Screenshots exigem aplicação, autenticação e banco em execução; devem ser gravados em `artifacts/visual-qa/screenshots`. O ambiente desta execução não disponibilizou o SDK `dotnet`, portanto não foi possível iniciar a aplicação nem gerar screenshots autenticados.

## Consistência e validação

O doctor agora detecta hero ausente nas famílias operacionais, tabelas e cards crus, botão não padronizado, empty-state indireto via revisão existente, tupla inline em `foreach`, `@media` Razor, CSS inline e ícones desconhecidos.

Execute:

```bash
dotnet clean InovaGed.sln
dotnet restore InovaGed.sln
dotnet build InovaGed.Web/InovaGed.Web.csproj -v:minimal
dotnet build InovaGed.Environment.Doctor/InovaGed.Environment.Doctor.csproj -v:minimal
dotnet build InovaGed.sln -v:minimal
dotnet run --project InovaGed.Environment.Doctor -- ui-consistency
```

Depois, autentique-se e valide as rotas de `tools/visual-qa/routes.json`, confirmando que nenhuma resposta é HTTP 500.
