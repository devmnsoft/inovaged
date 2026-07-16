# Relatório — correção de timeout de regex em requisições suspeitas

## Causa

O middleware usava uma expressão regular monolítica com alternativas amplas, incluindo `appsettings(.*)?`, o que podia produzir backtracking excessivo e `RegexMatchTimeoutException` em caminhos maliciosos.

## Correção

A inspeção passou a ser determinística:

- separa path de query string;
- limita comprimento antes de qualquer classificação;
- substitui `\` por `/`;
- remove espaços externos;
- rejeita caractere nulo;
- decodifica URL no máximo uma vez;
- colapsa barras duplicadas;
- compara segmentos e nomes de arquivo com `StringComparison.OrdinalIgnoreCase` e `HashSet` case-insensitive.

## Resultado esperado

- Paths sensíveis retornam `404` com JSON seguro.
- Paths acima de `MaxPathLength` retornam `414`.
- O middleware não usa regex para a classificação crítica, eliminando a causa do timeout.
- Logs truncam path, query e user-agent e registram hash do path normalizado em vez de depender do path completo.
