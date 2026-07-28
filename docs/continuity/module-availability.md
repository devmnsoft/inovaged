# Disponibilidade de continuidade
Todas as consultas são precedidas por `IModuleReadinessService`. GET indisponível exibe explicação com HTTP 200; POST retorna HTTP 409 ProblemDetails com `CONTINUITY_SCHEMA_NOT_READY` e correlationId, sem chamar repositórios.
