# Máquina de estados CMS

Estados: REQUESTED, WAITING_AGENT, CONTENT_ACCESSED, WAITING_CONFIRMATION, SIGNING, VALIDATING, COMPLETED, FAILED, CANCELLED, EXPIRED.

Transições destrutivas são bloqueadas: concluída, cancelada ou expirada não retorna ao fluxo ativo. Conteúdo é consumido apenas uma vez e a conclusão utiliza idempotency key e hash do payload.
