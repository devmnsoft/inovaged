# Stability Pass - Administração, Etiquetas, Retenção e Rotas Críticas

Checklist de homologação para executar após `database/apply_all_required_migrations.sql` e iniciar a aplicação com um usuário autorizado. Uma resposta `200` ou um redirecionamento de autenticação/autorização é aceitável; resposta `500` nunca é aceitável.

## Pré-condições

- [ ] `dotnet build InovaGed.sln -v:minimal` conclui sem erros.
- [ ] A aplicação inicia sem `AggregateException` ou falha de validação do container.
- [ ] O script consolidado foi aplicado com `psql -v ON_ERROR_STOP=1 -f database/apply_all_required_migrations.sql`.
- [ ] `/SchemaHealth` indica **OK** nos itens críticos de schema e em **System Health > Dependency Injection**.
- [ ] O teste é feito com um tenant válido e um usuário autorizado, preservando isolamento e políticas de acesso.

## Rotas

| Rota | Resultado esperado | Status | Observações |
| --- | --- | --- | --- |
| `/` | 200 ou redirecionamento autorizado | [ ] | Sem erro 500 |
| `/Administration` | 200; hero, métricas e cards estilizados | [ ] | Sem layout cru ou link quebrado |
| `/Administration/Health` | 200 | [ ] | Sem erro 500 |
| `/Administration/Readiness` | 200 | [ ] | Sem erro 500 |
| `/SchemaHealth` | 200; pendências reais e diagnóstico de DI | [ ] | Sem erro 500 |
| `/SchemaHealth/FixScript` | 200 ou redirecionamento autorizado | [ ] | Script útil e não destrutivo |
| `/Labels` | 200 | [ ] | Sem erro 500 |
| `/Labels/PrintWizard` | 200; wizard e painel de prévia estilizados | [ ] | Sem erro Razor ou layout cru |
| `/Labels/History` | 200 | [ ] | Sem erro 500 |
| `/Labels/LocDesk` | 200 | [ ] | Sem erro 500 |
| `/Labels/LocDeskBox` | 200 com `boxId` válido ou resposta 4xx controlada | [ ] | Caixa vazia/mista e datas nulas não causam 500 |
| `/Labels/LocDeskFolder` | 200 com `docId` válido ou resposta 4xx controlada | [ ] | Sem erro 500 |
| `/LabelTracking/Scanner` | 200 | [ ] | Sem erro 500 |
| `/LabelTracking/Inventory` | 200 | [ ] | Sem erro 500 |
| `/Retention` | 200 | [ ] | Sem erro 500 |
| `/RetentionDestination` | 200 | [ ] | Sem falha de DI |
| `/RetentionCase` | 200 | [ ] | Sem erro 500 |
| `/InstrumentVersions` | 200 | [ ] | Sem falha de DI |
| `/Loans` | 200 | [ ] | Sem erro 500 |
| `/Physical/Boxes` | 200 | [ ] | Sem erro 500 |

## Inspeção visual e logs

- [ ] Administração e Print Wizard mantêm leitura adequada em desktop e notebook.
- [ ] Cards administrativos indisponíveis aparecem desabilitados, com “Em implantação” e motivo, sem URL acionável.
- [ ] Os botões Voltar, Pré-visualizar, Registrar e imprimir e Imprimir em lote estão visíveis e organizados.
- [ ] Ícones Atlas conhecidos, aliases e nomes `bi-*` não geram warnings repetitivos.
- [ ] Falhas PostgreSQL `42P01` e `42703` mostram objeto, rota/action, correlation ID, script recomendado e link para `/SchemaHealth`.
