# Premium UI Pass 4 — Administração e Etiquetas

## Telas alteradas

- **Administração:** a central executiva ganhou hero, status do ambiente, indicadores, módulos agrupados e orientação técnica. Usuários, tenants, segurança, migrations e workers agora possuem views explícitas e seguem o mesmo shell administrativo.
- **Etiquetas:** a central operacional reúne as oito jornadas principais; o assistente usa stepper, alerta de rastreabilidade, formulário, resumo e prévia; LocDesk mantém edição e preview lado a lado; histórico reúne indicadores, filtros, tabela e estado vazio; caixas e documentos compartilham o acabamento premium.
- **Impressão LocDesk:** as views sem layout e o CSS em milímetros foram preservados. Menu, barra de ações e controles continuam ocultos por `@media print`, mantendo QR Code e bordas.

## Componentes criados

- `_PremiumHero`, `_PremiumKpiCard`, `_PremiumActionCard`, `_PremiumTableShell` e `_PremiumStepper`.
- `_PremiumAlert` e `_PremiumEmptyState` foram consolidados/reutilizados em vez de duplicados.
- Os componentes são deliberadamente simples e recebem dados por `ViewData`, reduzindo risco de compilação Razor e permitindo adoção incremental.

## CSS alterado

- `inovaged-design-system.css`: hero, KPI, ação e stepper reutilizáveis.
- `administration-premium.css`: grid executivo de indicadores e rodapé de orientação.
- `labels-premium.css`: cards operacionais, previews, KPIs, filtros, tabelas e estados vazios.
- `locdesk-labels.css`: dimensões físicas, contraste, borda e regras exclusivas de impressão permanecem como fonte de verdade.

## Antes/depois percebido

Antes, as operações dependiam visualmente de cards e tabelas Bootstrap genéricos, com pouca diferenciação entre navegação, status e ação. Depois, Administração comunica governança e saúde do ambiente em camadas; Etiquetas comunica fluxo operacional, prévia e auditoria; ações primárias e estados têm hierarquia consistente.

## Pendências visuais restantes

- Validar a escala física em todas as impressoras homologadas pelo cliente.
- Revisar textos longos com dados reais de produção e resoluções abaixo de 360 px.
- Capturar baselines adicionais com tenants que tenham grandes volumes de histórico.

## Como validar

```bash
dotnet clean InovaGed.sln
dotnet restore InovaGed.sln
dotnet build InovaGed.Web/InovaGed.Web.csproj -v:minimal
dotnet build InovaGed.sln -v:minimal
```

Com um usuário autorizado, abrir `/Administration`, as cinco áreas administrativas, `/Labels`, `/Labels/PrintWizard`, `/Labels/LocDesk`, `/Labels/History`, `/Labels/Boxes` e `/Labels/Documents`. No LocDesk, gerar prévia de pasta e caixa, verificar QR/borda e usar a pré-visualização de impressão em escala 100%.
