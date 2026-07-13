# Workers multi-tenant

Workers devem usar `ITenantCatalog` para listar tenants ativos e processar um tenant por vez em escopo DI separado. O tenant não deve ficar em variável global estática.

## Contratos adicionados
- `ITenantCatalog`: catálogo de tenants ativos.
- `ITenantExecutionContext`: tenant e correlationId da execução.
- `IJobExecutionLock`: lock por tenant/job/janela via PostgreSQL advisory lock.
- `ISystemUserProvider`: usuário técnico explícito para automações.

## Estado operacional
`/SystemHealth/Workers` lê `ged.worker_execution_state` quando a migration estiver aplicada. O painel não usa valores fictícios.

## Padrão recomendado
1. Listar tenants ativos.
2. Criar escopo por tenant.
3. Obter lock por tenant/job.
4. Registrar início/fim, sucesso/erro, duração, processados e correlationId.
5. Continuar demais tenants quando um falhar.
