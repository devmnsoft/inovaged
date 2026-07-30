# Seleção do Golden Master visual

## Método

A seleção foi feita por inspeção do histórico com `git log --follow`, comparação do markup de Login, `_Layout`, sidebar e folhas de estilo e análise dos pais de merges de reformulação. Nomes de commits não foram usados como prova. A referência visual é **0aac61be26eebc23e8d4c7bca586f9a5dfd5f79d**; regras, rotas e segurança continuam vindo da linha atual.

## Candidatos

| Candidato | Data | Estrutura e identidade | Resultado |
|---|---|---|---|
| `0e0de412` | 2026-07-27 | Shell legado denso, login completo, CSS ainda centralizado; responsividade irregular. | Rejeitado: anterior ao catálogo de UX. |
| `29d2bc95` | 2026-07-28 | Introduziu shell e navegação contextual, porém manteve login legado excessivamente decorativo. | Rejeitado: transição incompleta. |
| `0aac61be` | 2026-07-28 | Shell claro, identidade azul/verde, login institucional em duas colunas, dashboard/GED coerentes e breakpoints explícitos. | **Selecionado**: melhor equilíbrio entre fidelidade, hierarquia e adaptação. |
| `98ec7383` | 2026-07-28 | Conserva a fundação premium e amplia governança, mas começa a espalhar responsabilidades visuais. | Rejeitado: não melhora a referência visual. |
| `3591fd7b` | 2026-07-29 | Funcionalidades SRE sobre a mesma base; maior densidade técnica em superfícies administrativas. | Rejeitado: posterior à referência e sem ganho visual. |
| `c4d85c54` | 2026-07-30 | Login reduzido de 270 para 31 linhas e gate `premium-ui-playwright` sem navegador. | Rejeitado: regressão da PR #308. |

## Evidências da regressão

O merge da PR #308 (`c4d85c54`) substituiu a experiência de Login detalhada por markup comprimido e aceitou a UI por existência de Markdown. O job não restaurava, iniciava nem autenticava a aplicação e não instalava Chromium. Portanto, seu sucesso não demonstrava renderização, responsividade ou ausência de regressão.

## Decisão operacional

O Golden Master governa proporções, densidade, identidade e hierarquia. A implementação atual governa autenticação, autorização, tenancy, observabilidade, rotas e serviços. Baselines somente podem ser atualizados por fluxo explícito, com comparação e aprovação humana.
