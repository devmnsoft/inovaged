# Formato de Portabilidade InovaGED

Raiz: `inovaged-export/`. Arquivos obrigatórios: `manifest.json` e `checksums.sha256`. Formatos aceitos: JSON, NDJSON, CSV, XML quando necessário e arquivos originais em ZIP64.

O manifesto versão `1.0` contém exportação, tenant, escopo, solicitante, UTC, aplicação, schema, contagens, algoritmos, arquivos, checksums, inconsistências, opções, estado e correlation ID. Senhas, hashes, tokens, cookies, strings de conexão e chaves são proibidos.

Verificação: `InovaGed.Portability.Verifier <pasta>` retorna código 0 quando válido e não-zero quando inválido.
