# Diagnóstico técnico para o InovaGED Guardião

## Estrutura da solução
- `InovaGed.Domain`: entidades e primitivos.
- `InovaGed.Application`: contratos, DTOs, regras de aplicação, auditoria, upload, GED, OCR, PACS, retenção e empréstimos.
- `InovaGed.Infrastructure`: Dapper/PostgreSQL, workers, storage, OCR, preview, PACS e serviços concretos.
- `InovaGed.Web`: MVC/Razor, autenticação, autorização, filtros, hubs e composição DI.
- `InovaGed.Application.Tests`: testes unitários existentes.
- `database`: migrations idempotentes e diagnósticos SQL.

## Módulos e funcionalidades encontradas
GED com pastas/documentos/versões; upload simples, lote e chunked; OCR e preview por filas; classificação e temporalidade; empréstimos; acervo físico; assinatura; busca hospitalar; inteligência hospitalar; auditoria; logs; health checks; PACS básico.

## Fluxos principais
1. MVC valida policy/perfil e obtém tenant do usuário atual.
2. Serviços de aplicação orquestram regras.
3. Repositórios Dapper acessam `ged.*` com parâmetros.
4. Storage grava originais, previews e temporários.
5. Workers processam OCR, preview, upload pendente, retenção e qualidade.
6. Auditoria registra eventos com tenant, usuário e correlationId quando disponível.

## Tabelas utilizadas
Principais: `ged.document`, `ged.document_version`, `ged.folder`, `ged.document_search`, `ged.ocr_job`, `ged.upload_batch`, `ged.upload_batch_item`, `ged.upload_chunk_session`, `ged.app_audit_log`, `ged.security_access_failure_log`, `ged.retention_*`, `ged.loan*`, `ged.box*`, `ged.physical_*`, `ged.schema_migration_history`.

## Integrações e workers
- OCR: `OcrWorker`, `OcrAutoSchedulerWorker`.
- Preview: `PreviewWorker`.
- Upload: reconciliação e processamento GED.
- Retenção/qualidade: workers diários.
- PACS: `PacsIntegrationService` com diretório inbound.

## Riscos técnicos
- Havia senha PostgreSQL em `appsettings.json`.
- Seed estava habilitado por padrão e continha usuários demonstrativos.
- Certificados autoassinados internos estavam habilitados por padrão.
- Conflito de fuso entre `America/Sao_Paulo` e `America/Belem`.
- Alguns workers ainda possuem tenant configurado estaticamente, exigindo evolução para varredura de tenants ativos, lock por tenant e checkpoint.
- Controllers GED concentram muitas ações; risco de regras duplicadas entre controller, JS e repositório.
- Consultas com `ILIKE '%texto%'` e dashboards exigem índices/limites em bases grandes.
- PACS ainda precisa quarentena, dead-letter e reconciliação idempotente completa.

## Duplicidades e controllers grandes
`GedController` e fluxos correlatos concentram upload, listagem, movimentação e ações AJAX. A movimentação já possui serviços dedicados (`DocumentMoveService`, `GedFolderMoveService`), mas a governança deve impedir que regras voltem a ser implementadas no JavaScript/controller.

## SQLs frágeis, concorrência e multi-tenant
Há uso amplo de Dapper parametrizado, mas pontos críticos precisam de locks transacionais, versão/row version e `tenant_id` em todos os filtros. Movimentação, duplicidade, uploads chunked e jobs são os maiores pontos de concorrência.

## Possíveis falhas de autorização
A matriz de policies está documentada e aplicada em controllers principais, mas ações AJAX/documentos sigilosos devem continuar sendo validadas no backend, não apenas por menu. Quebra de vidro requer permissão própria, justificativa, auditoria legal e revisão.

## Lacunas da PoC e testes ausentes
Faltam testes integrados com PostgreSQL para tenant, autorização, movimentação concorrente, upload chunked fora de ordem, PACS, auditoria obrigatória/outbox e regras Guardião com evidências persistidas.

## Proposta de alterações
1. Remover segredos e endurecer configuração insegura.
2. Desabilitar seed por padrão e bloquear em produção.
3. Padronizar timezone em `America/Belem` com UTC no banco.
4. Criar base desacoplada do Guardião com tabelas idempotentes, serviço Dapper e tela somente com dados reais.
5. Evoluir workers para multi-tenant com locks/checkpoints em etapa seguinte.
6. Ampliar testes de movimentação, duplicidade, upload, PACS e Guardião.
