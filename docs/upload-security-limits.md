# Limites de segurança de upload

Uploads devem usar limites configuráveis, streaming, hash incremental, cancelamento, quota por tenant, controle de concorrência, validação MIME/extensão e remoção de temporários em `finally`. São proibidos `MaxRequestBodySize = null`, `long.MaxValue` e `int.MaxValue`. Valores de referência: request 2048 MiB, arquivo 512 MiB, multipart value 4 MiB, headers 64 KiB, quatro uploads por tenant e 20 GiB temporários.
