# Segurança operacional

A raiz de backup deve ser absoluta, gravável e fora de qualquer segmento `wwwroot`. A composição do caminho usa apenas GUIDs, confirma permanência sob a raiz e organiza `global/<set>` ou `tenants/<tenant>/<set>`. Artefatos finais só aparecem após validação e rename; parciais são compensados em falha. Retenção apenas marca candidatos e preserva legal hold e o último backup válido; exclusão física permanece desabilitada por padrão.
