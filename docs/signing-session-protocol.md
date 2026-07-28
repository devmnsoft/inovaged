# Protocolo de sessão de assinatura

O servidor cria sessão por tenant, documento e versão; calcula SHA-256 dos bytes reais; gera nonce, token de conteúdo e token de conclusão; persiste somente hashes; e conclui de forma transacional com idempotência e proteção anti-replay.
