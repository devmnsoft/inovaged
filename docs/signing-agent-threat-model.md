# Threat model do Signing Agent

## Ativos

- Documento e versão documental autorizada.
- Tokens de conteúdo e conclusão.
- Pairing token local.
- Certificado público e chave privada mantida no Windows Certificate Store.
- Sessão, evidências, cadeia e validation checks no PostgreSQL.

## Fronteiras

- Navegador autenticado ↔ servidor InovaGed.
- Navegador ↔ Signing Agent em loopback.
- Signing Agent ↔ servidor autorizado via HTTPS.
- Servidor ↔ PostgreSQL/storage.

## Ameaças e mitigação

| Ameaça | Mitigação | Teste esperado |
| --- | --- | --- |
| Site malicioso chama o agente | Pairing por origem exata, token em header e nonce anti-replay | origem divergente rejeitada |
| Replay de token | tokens de uso único armazenados como hash | segunda conclusão rejeitada |
| SSRF/DNS rebinding | allowlist, resolução DNS e validação de IP/redirecionamento | IP privado e redirect privado rejeitados |
| Confirmação silenciosa | confirmação humana local obrigatória | HTTP direto não assina |
| Certificado incorreto | comparação certificado incorporado/enviado e Key Usage | certificado divergente/sem uso rejeitado |
| Documento alterado | hash SHA-256 recalculado em streaming | assinatura sobre conteúdo alterado inválida |
| Tenant cruzado | consultas por `tenant_id`, documento, versão, sessão e assinatura | tenant A não acessa B |
| LGPD/CPF completo | persistência mascarada + HMAC-SHA-256 com chave externa | ausência de CPF completo em logs/evidências |
