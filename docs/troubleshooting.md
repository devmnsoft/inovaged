# Troubleshooting do InovaGED

## Upload não aparece na pasta

Confira batch, item, pasta destino, versão documental, permissões de storage e logs do upload.

## OCR com erro

Verifique fila, arquivo original, worker, dependência externa, timeout e mensagem registrada.

## Preview não abre

Valide geração, caminho físico, MIME type, versão documental e autorização do usuário.

## Busca não encontra

Confira filtros, tenant, OCR concluído, índices e dados cadastrais do documento.

## Menu não aparece

Revise perfil, permissões, claims, layout e proteção no controller.

## IIS 500.19

Revise `web.config`, runtime instalado, módulos ASP.NET Core, permissões e herança de configuração.

## PostgreSQL column not found

Aplique migrations pendentes, confirme schema `ged` e revise scripts executados no ambiente.

## Pool de conexão

Monitore conexões abertas, consultas longas, timeouts e paralelismo de workers.

## Validação e atualização do banco de dados

Quando uma tela crítica retornar a mensagem **“Estrutura do banco de dados desatualizada. Execute as migrations do sistema.”**, trate como incompatibilidade de schema antes de investigar regra de negócio.

### Corrigir erro 42703 (column not found)

1. Identifique no log a coluna ausente, o `SqlState=42703` e o `CorrelationId`.
2. Execute:

```bash
psql "$CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/apply_all_required_migrations.sql
```

3. Abra `/SystemHealth/Schema` e confirme que `ged.document_version.uploaded_at_utc`, campos de documento parcial, OCR e logs estão presentes.

### Corrigir erro 42P01 (relation not found)

1. Identifique no log a tabela ausente, o `SqlState=42P01` e o `CorrelationId`.
2. Reaplique o script master idempotente.
3. Rode `database/diagnostics/diagnostico_schema_ged.sql` para listar tabelas/colunas reais do schema `ged`.

### Telas protegidas por diagnóstico

As rotas `/Ged`, `/HospitalDocuments`, `/SystemLogs`, `/UploadBatch` e `/UploadChunk` não devem exibir stack trace para usuário final quando faltarem tabelas/colunas; elas retornam mensagem amigável e registram o script sugerido nos logs.

## Falha controlada de configuração no startup

Se a aplicação encerrar com **“Configuração obrigatória ausente ou insegura”**, verifique os logs `StartupConfiguration` e corrija variáveis de ambiente. Nunca cole senha completa em tickets; use apenas o valor mascarado exibido em `/SystemHealth/SecurityConfiguration`.

## OCR/Preview indisponível

A resolução de executáveis deve seguir: valor configurado, variável de ambiente, diretórios conhecidos, `PATH` e, por fim, indisponibilidade. Recurso opcional ausente não deve derrubar o GED inteiro; apenas o worker dependente deve ficar desabilitado ou em alerta.
