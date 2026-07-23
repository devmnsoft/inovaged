# CMS Atomic Completion

A conclusão CMS passa a abrir uma unidade de trabalho explícita no `CmsSigningOrchestrator`, chamando `BeginAsync`, `CommitAsync` e `RollbackAsync`. A meta transacional é manter lock, consumo da capability de conclusão, idempotência, assinatura, validation run, checks, cadeia, evidências, eventos e status final dentro do mesmo boundary.

A próxima etapa de endurecimento deve mover todos os repositórios participantes para overloads com `IDbConnection`, `IDbTransaction` e `CancellationToken`, para que nenhum participante abra conexão própria durante a conclusão.
