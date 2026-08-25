# Premium UI Pass 5 — Acabamento Final Administração e Etiquetas

## Telas revisadas

O passe cobre as centrais e páginas internas de **Administração** (`Index`, `Users`, `Tenants`, `Security`, `Migrations` e `Workers`) e **Etiquetas** (`Index`, `PrintWizard`, `LocDesk`, `History`, `Boxes`, `Documents` e os layouts de impressão LocDesk).

## CSS consolidado

`inovaged-design-system.css` é a fonte dos tokens de cor, superfície, borda, tipografia, raios e sombras. Ele também oferece primitivas estáveis `ig-page-shell`, `ig-page-hero`, `ig-card`, `ig-kpi-card`, `ig-action-card`, `ig-toolbar`, `ig-table-shell`, `ig-table`, `ig-empty-state`, `ig-alert-panel`, `ig-status-badge` e `ig-page-footer-note`. `administration-premium.css` e `labels-premium.css` complementam apenas a composição específica de cada módulo.

## Componentes criados ou alterados

- `_PremiumEmptyState` apresenta ícone, título, orientação e ação opcional sem inventar registros.
- `_PremiumAlert` aceita os tons `info`, `success`, `warning`, `danger`, `schema`, `migration` e `print`, além de recomendação e link opcionais.
- Cards, KPIs, stepper, tabelas e previews Atlas continuam reutilizáveis e usam os mesmos tokens.

## Melhorias por rota

- `/Administration`: hero executivo, atalhos operacionais, status do ambiente, KPIs, áreas de governança e orientação final.
- Páginas internas de Administração: retorno contextual, filtro, tabelas protegidas, badges e estados vazios.
- `/Labels`: hero operacional e oito acessos com descrição, status e ação.
- `/Labels/PrintWizard`: cinco etapas, formulário, resumo, preview e hierarquia de ações.
- `/Labels/LocDesk`: formulário e preview lateral com borda, marca, QR, controle e volume destacados; o texto homologado **ARQUIVO LOCDESCK ANANINDEUA** foi preservado.
- `/Labels/History`: cinco KPIs calculados apenas sobre dados reais, filtros, tabela auditável e aviso de schema acionável.
- `/Labels/Boxes` e `/Labels/Documents`: seleção, filtros, estados de dados e acesso ao fluxo de impressão.

## Ícones ajustados

Os fluxos usam nomes semânticos do catálogo (`users`, `building`, `key`, `shield`, `database`, `git-branch`, `activity`, `alert-triangle`, `tag`, `printer`, `qr-code`, `box`, `file-text`, `history`, `map-pin` e `settings`). O registro Atlas mantém aliases para que nomes semânticos conhecidos não causem erro em runtime.

## Responsividade e impressão

Grids reduzem para uma coluna em telas estreitas, toolbars e alertas quebram sem sobreposição, tabelas mantêm rolagem horizontal e previews respeitam a largura disponível. Os layouts LocDesk de impressão preservam dimensões em milímetros, bordas e QR Code e removem a navegação pelo layout de impressão existente.

## Pendências visuais restantes

Dados, permissões e alertas exibidos dependem do tenant e do schema disponíveis no ambiente. A validação autenticada deve usar um tenant representativo; estados vazios são intencionais e não são preenchidos com dados fictícios.

## Como validar

1. Execute `dotnet clean InovaGed.sln` e `dotnet restore InovaGed.sln`.
2. Execute os builds do projeto web e da solução.
3. Inicie o site com a configuração local válida e autentique um administrador.
4. Abra todas as rotas listadas acima em 1366×768, 1920×1080 e 390×844.
5. Gere as prévias LocDesk de pasta e caixa; confirme escala de 100%, borda, QR Code e ausência de navegação na impressão.
6. Confirme que migrations pendentes são apresentadas como alerta real, sem mascarar a falha.
