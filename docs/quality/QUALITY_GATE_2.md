# Quality Gate 2.0 - Testes, Segurança, Performance e Regressão

## Objetivo
Impedir regressões conhecidas de Razor, materialização Dapper, migrations, autorização, isolamento por tenant, UI e consultas ilimitadas antes do deploy.

## Como executar
`dotnet run --project InovaGed.Environment.Doctor -- quality-gate --no-db-required` agrega `route-smoke`, `ui-consistency`, `dapper-safety`, `migration-consistency`, `security-scan`, `tenant-isolation`, `performance-check`, `razor-safety`, `icon-check` e prontidão estática. Cada check também pode ser executado isoladamente com esse nome.

## Interpretação e correção
- **FAIL** bloqueia a entrega; **WARNING** exige triagem registrada; **PASS** tem evidência automática.
- **Razor:** remova `Title` de ViewData de partial, crie coleções tipadas antes de `foreach`, use CSS externo/`@@media`, um único campo canônico e `type` em botões.
- **Dapper:** materialize em `DbRow` mutável e converta explicitamente datas, GUIDs e enums.
- **Migrations:** mantenha catálogo/aplicador sincronizados e SQL idempotente; operações destrutivas precisam de allowlist revisada.
- **Segurança:** aplique política de autorização e antiforgery, valide exports e nunca persista token puro ou segredo.
- **Tenant:** toda consulta operacional deve demonstrar `tenant_id` ou isolamento estrutural documentado.
- **Performance:** proíba `select *`; use `page >= 1`, `pageSize` padrão 25/50 e máximo 100, com `LIMIT/OFFSET`.

## CI e relatórios
O workflow `.github/workflows/dotnet-quality-gate.yml` restaura, compila, testa e roda offline. Evidências JSON/Markdown são gravadas em `artifacts/quality-gate/` e exibidas em `/Administration/Quality`. Checks dependentes de banco produzem warning controlado no modo offline, nunca sucesso fictício.
