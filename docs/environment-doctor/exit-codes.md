# Exit codes

* `0`: pronto.
* `1`: warnings ou itens não verificáveis não bloqueantes.
* `2`: falha bloqueante.
* `3`: comando/configuração inválida.
* `4`: erro inesperado sanitizado.

CI rejeita 2, 3 e 4; código 1 só deve ser aceito em ambiente não produtivo explicitamente definido.
