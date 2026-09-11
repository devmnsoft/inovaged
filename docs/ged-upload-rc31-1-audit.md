# Diagnóstico do upload GED — RC31.1

| Caminho | HTTP/payload | Estado JS | Visibilidade/retry | Diagnóstico |
|---|---|---|---|---|
| Normal/batch file | 2xx JSON | success | linha e resumo; ações documentais | preserva documentId/versionId |
| Falha funcional | 4xx JSON normalizado | error | alerta, linha, detalhes e retry conforme contrato | corrigido |
| Chunk start/part/complete | 2xx/4xx JSON ou resposta inesperada | success/error | linha e detalhes | metadata inclui classificação |
| Fallback legado | contrato compatível do BulkUploadSingle | success/error | mesmos canais do batch | parsing compartilhado |
| Duplicidade | 400 com confirmação | duplicate/error | decisão explícita | sem upload automático |
| Timeout | sem resposta | timeout | alerta persistente, linha, detalhes e retry | antes era `aborted` sem detalhes visíveis |
| Rede/abort | status 0 | aborted | alerta persistente, linha, detalhes e retry | predicado central cobre aborted |
| Cancelamento | local | cancelled | mensagem distinta e detalhes | não confunde com rede |
| 401 | JSON/HTML | error | sessão expirada, sem retry | normalizado |
| 403 | JSON | error | permissão funcional, sem retry | normalizado |
| 413 | JSON/HTML/proxy | error | orienta partes/redução, sem retry cego | normalizado |
| 429 | JSON/texto | retrying/error | backoff limitado e mensagem | normalizado |
| 5xx/não JSON | JSON/HTML/texto | error | resposta inesperada + HTTP | HTML não é persistido |
| Extensão/tamanho | 400/413 | error | motivo funcional; retry bloqueado | visível |
| Pasta/permissão | 400/403 | error | motivo funcional e atendimento | visível |
| Storage/banco/OCR/preview/schema | 4xx/5xx seguro | error/warning | alerta e detalhes seguros | exception completa somente no log |
