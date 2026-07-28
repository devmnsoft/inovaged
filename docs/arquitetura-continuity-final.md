# Arquitetura final — Continuidade e Portabilidade

- Domain permanece sem dependência de PostgreSQL.
- Application contém contratos, DTOs, interfaces de autorização e orquestração.
- Infrastructure contém Dapper, Npgsql, SQL, filesystem, `pg_dump`, `pg_restore`, provider de backup, verificador e composição DI.
- Web, WebApi e Worker compõem serviços e expõem operações sem duplicar resolução de tenant.

## Tenant

`IAdministrativeTenantScopeResolver` é contrato de Application e `AdministrativeTenantScopeResolver` é implementação em Infrastructure. Administradores locais ficam restritos ao tenant da identidade; administradores globais podem informar tenant; tentativas cruzadas retornam 403/404 seguro conforme o endpoint.
