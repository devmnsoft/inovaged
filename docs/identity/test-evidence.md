# Evidência de testes

Data: 2026-07-28.

* A imagem local não possui `dotnet`, `psql` ou `docker`; builds e testes locais ficaram bloqueados por limitação do ambiente.
* Foi criado o job `identity-auth-integration` com PostgreSQL 16. Ele aplica a fixture legada, executa duas vezes a migration, valida índices/trigger/view e a consulta canônica, roda contratos de autenticação e rejeita zero testes.
* A execução remota do workflow somente poderá ser avaliada após o push/abertura da PR. A PR deve permanecer draft até todos os jobs ficarem verdes.
