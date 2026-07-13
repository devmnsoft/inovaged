# Configuração do InovaGED

## appsettings

Configure `ConnectionStrings`, `Storage`, `DocumentUpload`, `Ocr`, `Preview`, `Security` e parâmetros de workers conforme o ambiente.

## Banco

- Banco PostgreSQL com schema `ged`.
- Migrations aplicadas em ordem.
- Índices de busca, OCR, dashboard e upload habilitados.

## Storage

- Diretórios para originais, previews, temporários e chunks.
- Permissões de leitura/escrita para o usuário do processo.
- Monitoramento de espaço livre e retenção.

## OCR e preview

- Worker ativo.
- Dependências externas configuradas.
- Timeouts e tamanho máximo alinhados ao upload.
- Logs habilitados para falhas e reprocessamentos.

## Segurança

- Perfis cadastrados.
- Permissões validadas no menu e no controller.
- Usuários reais vinculados ao tenant correto.

## Validação e atualização do banco de dados

O InovaGED valida o schema no startup quando `Database:ValidateSchemaOnStartup=true`. A configuração recomendada inicial é:

```json
"Database": {
  "ValidateSchemaOnStartup": true,
  "FailFastOnInvalidSchema": false
}
```

Em homologação/produção estabilizada, altere `FailFastOnInvalidSchema=true` para impedir que a aplicação suba com tabelas ou colunas críticas ausentes.

### Aplicar migrations obrigatórias

1. Faça backup do banco.
2. Execute o pacote master a partir da raiz do repositório:

```bash
psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/apply_all_required_migrations.sql
```

3. Rode o diagnóstico estrutural, se necessário:

```bash
psql "$CONNECTION_STRING" -f database/diagnostics/diagnostico_schema_ged.sql
```

4. Acesse `/SystemHealth/Schema` com perfil `ADMIN` e confirme que tabelas e colunas críticas estão OK.

O script consolidado `database/migrations/2026_06_ged_schema_consolidation.sql` é idempotente: usa `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` e `CREATE INDEX IF NOT EXISTS`, podendo ser reaplicado em ambientes já existentes.

## Segredos e string de conexão

`InovaGed.Web/appsettings.json` não deve conter senha. Configure a senha por variável de ambiente ou User Secrets em desenvolvimento:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=inovaged;Username=inovaged;Password=<senha>;Pooling=true;Application Name=InovaGed" --project InovaGed.Web/InovaGed.Web.csproj
# ou variável de ambiente
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=inovaged;Username=inovaged;Password=<senha>;Pooling=true;Application Name=InovaGed"
```

Use `InovaGed.Web/appsettings.Example.json` como modelo sem credenciais reais.

## Seed e certificados internos

`SystemSeed:Enabled` é `false` por padrão. O seed só pode executar em `Development` ou em PoC explicitamente configurada com `SystemSeed:AllowInPoc=true`; em `Production` é bloqueado.

`Auth:AllowInternalSelfSignedCertificates` é `false` por padrão. Em `Production`, a aplicação bloqueia startup se a opção insegura for habilitada. Isso se aplica apenas a certificados internos autoassinados e não altera validação ICP-Brasil.

## Fuso horário

Datas persistidas devem ser UTC. A visualização usa o fuso do tenant quando disponível e fallback operacional `America/Belem` (`App:LocalTimeZoneId` e `Localization:DefaultTimeZone`).
