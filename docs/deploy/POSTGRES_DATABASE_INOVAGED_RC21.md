# Banco PostgreSQL dedicado — RC21

O uso do banco administrativo `postgres` gera apenas uma recomendação; esta release **não altera banco nem connection string automaticamente**.

## Ambiente novo

1. Defina uma senha forte apenas na sessão segura do `psql`/cofre; nunca edite o SQL versionado com senha real.
2. Como administrador, execute `database/manual/create_inovaged_database.sql` via `psql`.
3. Configure externamente `DefaultConnection` com `Database=inovaged` e a senha proveniente do cofre/variável de ambiente.
4. Execute as migrations homologadas pelo procedimento do projeto (por exemplo, `scripts/windows/apply-migrations.ps1`) contra o novo banco.
5. Valide `/DatabaseReadiness`, `/SchemaHealth`, `/status` e backup/restauração antes de promover.

Para migrar uma instalação existente, faça backup testado e planeje janela/rollback. Não execute o script de criação sobre produção sem aprovação. Logs devem conter no máximo host, porta, database e usuário; qualquer senha deve aparecer como `Password=****`.
