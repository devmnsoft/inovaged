# Retry e dead letter

Falhas incrementam `attempts` e usam `RETURNING status, attempts, max_attempts`. O evento grava o estado efetivamente retornado (`RETRY` ou `DEAD_LETTER`). A primeira linha sanitizada do erro é limitada antes da persistência. Cancelamento usa token interno de dez segundos, remove parciais, libera o lease ao transicionar para `CANCELLED` e propaga a exceção.
