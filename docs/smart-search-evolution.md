# Evolução da Busca Inteligente

## Diagnóstico da implementação anterior

A rota `/SmartSearch` já reutilizava `SmartSearchController`, `DocumentAssistantService`, `SmartSearchService`, `SmartQueryParser` e `SmartSearchRepository`. A recuperação usa o índice `ged.document_search_index`, com full-text do PostgreSQL e fallback para `ged.document_search`; as consultas sempre recebem `tenant_id`, paginação e `CancellationToken`. Pesquisas salvas já eram isoladas por tenant e usuário e excluídas logicamente.

Os principais problemas encontrados eram: protocolos perdiam barras, hífens e zeros na interpretação; frases entre aspas não tinham tratamento próprio; o trecho OCR era sempre o início do texto; sugestões não incluíam pesquisas salvas; respostas antigas de autocomplete poderiam competir; e o score bruto era apresentado como porcentagem, sem calibração. A materialização Dapper de `SmartSearchSavedSearch` foi conferida: aliases SQL coincidem com propriedades públicas, contadores e favoritos são não nulos por `coalesce`, e datas opcionais usam `DateTime?`.

## Regras entregues

- protocolos/códigos preservam sua representação (`001234/2026`, barras e hífens) e recebem prioridade sobre OCR vago;
- frases entre aspas recebem peso explícito em título e metadados;
- anos, `MM/AAAA` e meses por nome (por exemplo, `agosto de 2026`) produzem intervalos exclusivos e visíveis;
- ordenação aceita somente a lista segura `relevance`, `newest`, `oldest` e `title`, com desempate por documento;
- snippets são recortados perto da primeira evidência e continuam escapados pela interface;
- autocomplete tem debounce, cancelamento e controle de sequência, e mostra somente sinônimos do tenant e pesquisas salvas do próprio usuário;
- filtros interpretados são exibidos como chips e podem ser descartados para manter somente o texto original;
- o ranking interno deixou de ser apresentado como porcentagem de certeza.

## Consultas suportadas

- `protocolo 001234/2026`
- `"manutenção preventiva" 2025`
- `ofícios recebidos em agosto de 2026`
- `documentos sem OCR`

## Limites de validação

O ambiente de desenvolvimento desta execução não contém o SDK .NET nem uma instância PostgreSQL configurada. Portanto, build, fluxo HTTP, plano `EXPLAIN ANALYZE`, medição de latência e captura autenticada da página ficaram pendentes. Nenhum ganho de desempenho é declarado sem essas medições. A busca continua lexical e não depende de provedor externo de IA.
