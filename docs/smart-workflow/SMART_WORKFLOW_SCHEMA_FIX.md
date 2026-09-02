# SmartWorkflow — correção de schema

## Erro encontrado
A validação de startup indicava ausência de `smart_workflow_task`, `smart_workflow_event`, `smart_workflow_rule` e `smart_workflow_dashboard_snapshot`.

## Migration criada
`database/migrations/2026_09_02_smart_workflow_schema_fix.sql` é idempotente e cria as quatro tabelas, relacionamentos, índices e regras iniciais compatíveis com o serviço existente.

## Scripts atualizados
A migration foi registrada em `database/required_migrations.json` e incluída efetivamente ao final de `database/apply_all_required_migrations.sql`.

## Como aplicar
Execute a partir da pasta `database`, com `psql`: `psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f apply_all_required_migrations.sql`. O comando `\\ir` resolve os arquivos relativamente ao script.

## Como validar no SchemaHealth e DatabaseReadiness
Reinicie a aplicação, abra `/SchemaHealth` e `/DatabaseReadiness` e confirme que as quatro tabelas estão presentes. Também valide no PostgreSQL com `select to_regclass('ged.smart_workflow_task'), to_regclass('ged.smart_workflow_event'), to_regclass('ged.smart_workflow_rule'), to_regclass('ged.smart_workflow_dashboard_snapshot');`.
