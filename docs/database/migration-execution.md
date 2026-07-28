# Execução de migrations
Use `dotnet run --project InovaGed.Database.Migrator -- apply --verify`. A conexão vem de `ConnectionStrings__DefaultConnection`, user-secrets ou `DATABASE_URL`; nunca passe senha na linha de comando.

Hotfix por psql, nesta ordem:
```bash
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/migrations/2026_07_backup_continuity_portability.sql
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/migrations/2026_07_estabilizar_admin_continuity_ci.sql
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f database/assert_continuity_schema.sql
```
Em produção a aplicação Web apenas valida e bloqueia módulos incompatíveis; a execução é externa e autorizada. Rollback exige restauração do backup pré-implantação e do binário anterior.
