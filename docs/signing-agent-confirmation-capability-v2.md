# Signing Agent Confirmation Capability v2

A confirmação local deve usar capability própria: `/operations/{id}/confirm-ui?token=...`. O token deve ser armazenado como hash, expirar, ser de uso único e estar vinculado ao pairing e à operação. O formulário local deve usar antiforgery, cookie `HttpOnly`, `SameSite=Strict` e não depender apenas do GUID da operação.
