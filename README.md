# InovaGED

O InovaGED é uma plataforma ASP.NET Core MVC para Gestão Eletrônica de Documentos hospitalares, com organização por pastas, upload simples e em lote, OCR, preview, busca, classificação documental, empréstimos, auditoria, logs e inteligência documental baseada em registros processados.

## Módulos reais

- **Autenticação e segurança**: login por e-mail/CPF, perfis, permissões, auditoria e bloqueios por regra de acesso.
- **GED**: explorer de pastas, documentos, versões, movimentação, busca e ações documentais.
- **Upload**: envio simples, upload em lote, chunks para arquivos grandes, monitor de lote e reenvio de falhas.
- **OCR e preview**: filas reais de processamento, geração de visualização e consulta por texto reconhecido.
- **Classificação**: tipos documentais, regras arquivísticas, sugestões por conteúdo quando disponíveis e painel de pendências.
- **Busca hospitalar**: pesquisa por prontuário, paciente, APAC, termo do OCR e filtros de contexto.
- **Inteligência hospitalar**: indicadores calculados a partir de documentos e OCR concluído.
- **Solicitações/Loans**: solicitação, aprovação, entrega, devolução, cancelamento e trilha de auditoria.
- **Administração**: usuários, servidores, certificados, parâmetros, logs e relatórios.
- **Acervo físico**: lotes, caixas, localizações e mapa físico quando habilitados.

## Requisitos

- .NET SDK compatível com os projetos da solução.
- PostgreSQL com schema `ged` configurado.
- Storage local ou compartilhado para documentos, previews e temporários.
- LibreOffice instalado quando a conversão de documentos de escritório para preview estiver habilitada.
- Serviço de OCR configurado conforme `appsettings` e filas do banco.
- IIS/Windows Server ou Kestrel/reverse proxy para publicação.

## Instalação

```bash
git clone https://github.com/devmnsoft/inovaged.git
cd inovaged
dotnet restore
dotnet build InovaGed.sln
```

Configure a string de conexão e os caminhos de storage antes de executar a aplicação.

## Configuração

Principais grupos em `InovaGed.Web/appsettings.json`:

- `ConnectionStrings`: conexão PostgreSQL.
- `Storage`: raiz de documentos, previews, temporários e arquivos de lote.
- `DocumentUpload`: limites de tamanho, quantidade, paralelismo e timeout.
- `Ocr`: parâmetros de fila, worker, reprocessamento e status.
- `Preview`: geração, caminhos e dependências externas.
- `Security`: políticas de autenticação, perfis e proteção de rotas.

Nunca use valores sem origem operacional em produção. Cada tenant, usuário, pasta, documento e indicador deve existir no banco operacional.

## Banco de dados

Aplique migrations em ordem cronológica em ambiente controlado. Scripts relevantes ficam em `database/migrations` e diagnósticos em `database/diagnostics`.

Comandos úteis:

```bash
psql "$CONNECTION_STRING" -f database/migrations/20260601_upload_batch.sql
psql "$CONNECTION_STRING" -f database/migrations/20260603_upload_chunk.sql
psql "$CONNECTION_STRING" -f database/migrations/20260605_upload_ocr_partial_documents.sql
```

Antes de qualquer saneamento textual, gere backup e revise os SELECTs diagnósticos.

## Storage

- Garanta permissão de leitura/escrita para o identity pool do IIS ou usuário do serviço.
- Separe diretórios de documentos originais, previews, temporários e chunks.
- Monitore espaço livre, retenção de temporários e integridade dos arquivos.

## OCR e preview

- OCR deve usar jobs persistidos e status reais.
- Preview deve usar versões documentais existentes.
- Quando não houver OCR concluído, a interface deve informar indisponibilidade sem inventar conteúdo.
- Falhas devem ser registradas com correlationId e mensagem técnica suficiente para suporte.

## Publicação IIS e upload grande

- Ajuste `web.config`, `maxAllowedContentLength`, limites de request e timeout.
- Alinhe `FormOptions.MultipartBodyLengthLimit` aos limites do IIS.
- Para upload grande, use chunks e monitoramento de lote.
- Valide permissões de diretório após publicação.

## Workers

Workers de OCR, preview, reconciliação de upload e rotinas de diagnóstico devem processar filas reais, registrar logs e respeitar idempotência. Reenvios devem preservar documentos concluídos e reprocessar apenas itens elegíveis.

## Comandos úteis

```bash
dotnet clean InovaGed.sln
dotnet restore InovaGed.sln
dotnet build InovaGed.sln --no-restore
rg -n -i "termos_auditados" --glob '!**/bin/**' --glob '!**/obj/**'
```

## Troubleshooting rápido

- **Upload não aparece**: verifique lote, item, pasta destino, permissões de storage e logs.
- **OCR com erro**: consulte fila, status, worker, arquivo original e dependências externas.
- **Preview não abre**: confira versão documental, geração de preview, MIME type e permissões.
- **Busca não encontra**: valide OCR concluído, índices, filtros e tenant.
- **Menu não aparece**: confirme perfil, permissões e proteção no controller.
- **IIS 500.19**: revise `web.config`, módulos instalados e permissões.
- **PostgreSQL column not found**: aplique migrations pendentes e confira schema ativo.
- **Pool de conexão**: avalie limites, timeouts e consultas longas.

## Documentação

- `docs/manual-inovaged.md`
- `docs/arquitetura.md`
- `docs/configuracao.md`
- `docs/iis-upload-config.md`
- `docs/ocr-preview.md`
- `docs/perfis-e-permissoes.md`
- `docs/troubleshooting.md`

## Estabilização GED/OCR/Upload/Logs (Junho 2026)

Para validar uma instalação antes de operar o GED, acesse `/SystemHealth/Schema` com perfil `ADMIN`. O diagnóstico diferencia pendências críticas, recomendações de performance e itens opcionais. A aplicação só considera o schema incompatível quando faltam tabelas ou colunas críticas; se restarem apenas índices recomendados, o status exibido será "Schema funcional com recomendações de performance.".

A migration consolidada idempotente é `database/apply_all_required_migrations.sql`, que aplica `database/migrations/2026_06_ged_schema_consolidation.sql`. Os scripts usam `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS` e registram aplicação em `ged.schema_migration_history`, sem `DROP` e sem remoção de dados.

Após upload simples, em lote ou chunked, a resposta do backend inclui `documentId`, `versionId`, `resolvedFolderId`, `folderName`, `uploadedAtUtc` e `uploadedAtLocalFormatted` quando o documento é criado. A listagem do GED deve ser recarregada via AJAX pela pasta resolvida, sem exigir F5, mantendo a pasta ativa e destacando os documentos enviados.

A etiqueta "OCR disponível" só deve aparecer quando o status do job é `COMPLETED` e existe texto OCR não vazio. Estados pendente, processamento, erro, cancelado, concluído sem texto e sem OCR são exibidos separadamente.

## Estabilização de configuração e segurança (Julho 2026)

- `appsettings.json` não versiona senha PostgreSQL nem caminhos pessoais obrigatórios.
- Configure `ConnectionStrings__DefaultConnection` por variável de ambiente, User Secrets, IIS ou Docker.
- Em produção, configurações críticas inseguras bloqueiam o startup e aparecem em `/SystemHealth/SecurityConfiguration` com valores mascarados.
- O timezone padrão operacional é `America/Belem`; persistência deve permanecer em UTC e a apresentação deve usar o timezone do tenant.
- Seeds ficam desabilitados por padrão e só podem rodar em `Development` ou `PoC` com senha informada por variável `INOVAGED_DEV_SEED_PASSWORD`.
- Consulte `docs/configuracao-por-ambiente.md`, `docs/seguranca-configuracao.md` e `docs/workers-multitenant.md` antes da publicação.
