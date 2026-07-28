# Resolução de contexto multi-tenant

A auditoria encontrou `TenantSlug = "default"` no login e recuperação de senha. A remoção segura depende de catálogo e resolução validada por host/subdomínio, com fallback explícito apenas em instalação single-tenant. Este trabalho permanece pendente; valores livres do navegador não devem virar contexto confiável.
