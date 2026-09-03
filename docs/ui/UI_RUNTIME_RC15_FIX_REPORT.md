# UI/UX Stabilization RC15 — relatório de correção

## Incidente e causa

O dashboard do Acervo Físico consultava `reg_status` incondicionalmente. Instalações anteriores à evolução do schema retornavam PostgreSQL `42703`, interrompendo `/Physical/Dashboard`.

## Correções aplicadas

- **Schema-aware:** `DashboardAsync` consulta `information_schema.columns` para cada tabela física conhecida e só acrescenta o predicado ativo quando a coluna existe. Não há captura genérica nem ocultação de falhas.
- **Compatibilidade:** a migration idempotente `2026_09_03_physical_archive_reg_status_compat_fix.sql` adiciona `reg_status` às oito tabelas físicas, e foi registrada no manifesto e aplicador consolidado.
- **Botões:** PrintWizard e LocDesk usam POST nativo, antiforgery e `formaction`. Formulários de etiquetas não passam mais pelo bloqueio global do Atlas; o estado transitório possui restauração por timeout e `pageshow`.
- **Impressão:** a página mantém fallback inline para `window.print()` e o script externo progressivo.
- **PrintWizard:** a Conferência final recebeu texto orientativo, grid legível, textarea maior e rodapé de ações separado com Histórico.
- **History:** hero, seis KPIs, painel de filtros, badges, tabela responsiva, ações e empty state foram mantidos e refinados no padrão premium.
- **ClassificationPlan:** hero, seis indicadores, orientação/busca e sete cards operacionais organizam árvore, temporalidade, importação, comparação, revisão, versões e relatórios.
- **Administration:** título, subtítulo, saúde do ambiente, indicadores, grupos de módulos, alertas e ações rápidas seguem o padrão executivo.

## Validação

O comando `dotnet run --project InovaGed.Environment.Doctor -- ui-runtime-rc15` verifica migration, schema-awareness, ações nativas, fallback de impressão, recuperação de loading, rotas críticas e ausência de `@media` em Razor. As rotas `/Labels/History`, `/Labels/PrintWizard`, `/ClassificationPlan`, `/Administration` e `/Physical/Dashboard` fazem parte do route smoke.

## Pendências de ambiente

A aplicação real da migration e o diálogo nativo de impressão exigem, respectivamente, acesso a uma instância PostgreSQL e navegador gráfico. Devem ser confirmados na homologação seguindo o roteiro manual da entrega.
