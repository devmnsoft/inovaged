# RC20 — padronização visual

## Problemas encontrados
History, conferência final, Administração e Plano de Classificação precisavam permanecer alinhados ao sistema visual premium.

## Causa raiz
Evolução incremental das telas, com componentes legados ainda coexistindo.

## Arquivos alterados
Views de Labels, Administration e ClassificationPlan e folhas premium existentes.

## Correções aplicadas
Foram preservados hero, KPIs, cards, filtros, badges, tabela/árvore, empty states e hierarquia de ações. A conferência final usa tiles sem tabela.

## Como validar
Revisar as quatro rotas em desktop e mobile, com estados cheio e vazio, e executar o gate RC20.

## Evidências de build/publish
Contratos Razor do Doctor verificam os marcadores visuais; validação visual final requer navegador autenticado.

## Pendências
Capturar evidência no ambiente de homologação, pois este container não fornece runtime .NET nem sessão/dados do sistema.
