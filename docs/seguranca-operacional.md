# Segurança operacional

## Middleware de requisições suspeitas

O middleware classifica paths por regras determinísticas e registra eventos estruturados com:

- IP;
- método;
- path truncado;
- hash SHA-256 do path normalizado;
- tamanho original;
- query truncada;
- user-agent truncado;
- categoria;
- ação;
- timestamp UTC;
- correlationId.

## Respostas externas

- Caminhos suspeitos: `404` com `status`, `title` e `correlationId`.
- URI acima do limite: `414` com `status`, `title` e `correlationId`.

## Dados não registrados

O middleware não registra Authorization, cookies, senhas, tokens, connection strings, dados clínicos nem conteúdo documental.

## Próximo ciclo recomendado

Persistir os eventos em `ged.security_event`, adicionar painel `/SecurityOperations`, políticas de rate limiting e bloqueio temporário por comportamento em janela, com allowlist e auditoria administrativa.
