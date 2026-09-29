# Evidência de Reprodução (Fase 1) — bugs reproduzidos, causa raiz e veredictos corrigidos

Data: 2026-09-29 · Alvo HTTP: `http://localhost:5000` sobre `inovaged_repro` (instância web #B; log rodadas 4 em `trnsito\webapp3_r4_archived.log`) · Credenciais seed: `Inovaged@2026`.

## Método
- Probes via **curl.exe** e **.NET HttpClient** como fonte de verdade; **PowerShell 5.1 `Invoke-WebRequest` com multipart montado em byte[]** foi triplamente confirmado como artefato de cliente (abaixo). Regras de transporte: antiforgery header `RequestVerificationToken` (sem hífen) + `__RequestVerificationToken` no form; JSON via `--data-binary @arquivo` (escrito sem CRLF).
- Evidência = HTTP + corpo salvo + linha do log + estado de DB (`psql`) + armazenamento local (`trnsito\storage_repro`).
- Peculiaridade de telemetria observada: "POST X completed with 200" pode aparecer **antes** da reescrita 500 do ExceptionHandler — confiar nos corpos salvos + linhas de erro do log.

## Artefato de cliente PS 5.1 (veredicto final)
`Invoke-WebRequest` + `byte[]` multipart comanda comportamento inconsistente **por endpoint**:
1. `Expect: 100-continue` → resolvido com `[ServicePointManager]::Expect100Continue=$false`.
2. Mesmo corpo/headers: `/ProtocolRequests/New` → 400 "Erro 0", enquanto urlencoded idêntico → 200 e HttpClient multipart → 200.
3. Em `/Ged/UploadBatch/File`, **campos do form são descartados server-side** na montagem PS (batchId/folderId chegam vazios → resposta com `Guid.Empty`), mas **curl `-F` com os mesmos campos → 200** e documento criado.
4. Anomalia residual (cosmética para veredicto): em `/Protocols/{id}/RespondAdjustment` a mesma montagem PS **funcionou** (i1: transição commitada com texto do motivo antes do IOException pós-commit). Difícil de explicar; não afeta nenhum veredicto porque todos os endpoints têm ground truth por curl/HttpClient.
Conclusão: para probes multipart usar sempre curl ou HttpClient.

## A2 — Criar solicitação / responder ajuste (três modos de falha)
| # | Cenário | Sintoma | Causa raiz (pinned) |
|---|---|---|---|
| A2-1 | POST `/ProtocolRequests/New` sem arquivo | "sucesso silencioso" — número do protocolo não gera, erro nunca exibido | `ProtocolRequestService.GenerateProtocolNoAsync` insere `(tenant_id, module, year,…)` em `ged.code_sequence` cuja forma real é `(id, tenant_id, entity_name, prefix, current_value, padding,…)` → SQL 42703; `TempData["Err"]` configurado porém **nunca renderizado** em `New.cshtml` |
| A2-2 | POST com anexo cujo nome tem caractere ilegal Windows (ex.: `a\|b.txt`) | 500 **depois** do estado já commitado | Loop de arquivos em `ProtocolRequestsController.New` L93-101 e `ProtocolsController.RespondAdjustment` L105-114: `Path.GetFileName` sem sanitização; `IOException` em `LocalFileStorage.WriteWithHashesAsync` (L241) não tratada; resultado de `AddAttachmentAsync` ignorado |
| A2-3 | GET `New` sem modelo populado (caminho archived) | NRE duplo 500 | `vm` nulo usado em `New` L85; `New.cshtml` L77 sem guarda |

Correção planejada (Bloco A): forma `entity_name='PROTOCOL-{year}'`; sanitizar nomes antes de transição irreversível; validação de arquivos **antes** da mudança de estado; não ignorar resultado de `AddAttachmentAsync`; compensar órfãos de storage; renderizar `TempData["Err"]`.

## R4/R6 — RespondAdjustment (reprodução limpa)
- **Bug repro 1:** 500 pós-commit com nome ilegal (A2-2) — estado commitado + falha no anexo.
- **Bug repro 2:** **linhas duplicadas de auto-transição** em `protocol_request_history` (P3 `7a232c4b-4f37-449a-a4e3-00bedeb3e659`: ADJ_ANSWERED→ADJ_ANSWERED duplicado) — `AssignOrTransitionAsync` faz UPDATE incondicional sem guarda de status/idempotência.
- **AccessDenied one-off:** NÃO reproduzível hoje (classificado como instância intermitente #A); recomendação: logging request-scoped de `UserId/TenantId/roles`.
- Retry após erro (R6): funciona (nenhuma duplicação de documento/versão nesse caminho).

## H2/H3/H4/H4B — Retention (do dump legado)
- **H2:** vaza `23502` (violation de `retention_case.id`) direto no HTML — mensagem crua no lugar de mensagem segura.
- **H3:** lote HOSPITAL commita e retorna 500; faltam `RetentionDestination`/`Details.cshtml` e coluna `RetentionDestination`.
- **H4/H4B:** lacuna de permissão — papel legado `ARQUIVISTA` ≠ `ARQUIVISTAOPHIR`; POSTs de Temporalidade/Retention aceitos por usuário que deveria ser somente-leitura via URL direta. Correção Bloco A: políticas específicas por ação (não só por controller).

## U — Uploads (veredictos finais)
- **U1 Start:** contrato ok (exige `folderId`; erro estruturado com `success/canRetry/correlationId/requestedFolderId/resolvedFolderId/folderName/errorStep`) — 200 com pasta "Prontuários" `a26e4a8f-…`.
- **U2/U3 File:** falhas iniciais = artefato PS (campos perdidos) + round-4 com `folderId` não reenviado por arquivo. Com cliente adequado + `folderId` reenviado → **200**, documento `081bfa9a-…` criado (lote `c87e9bc9-…`).
- **U4 Finish falso sucesso (bug real):** lote `0817a7ae-…` com 2/2 arquivos rejeitados → `{"success":true,…,"status":{…COMPLETED,"total":2,"success":0,"failed":0}}`. Controle positivo: Finish do lote `c87e9bc9` (1/1 ok) reporta corretamente `success:true,total:1,success:1,createdDocuments[…]`. Contrato deve refletir falha total/nula.
- **U5–U8 Chunk (nunca funcionou end-to-end; dois bloqueadores independentes, ambos pinned):**
  1. **Drift de schema:** forma consolidada de `upload_session` (criada por `2026_06_ged_schema_consolidation.sql` L211-255) **não tem** `metadata_json` nem `batch_item_id`; o `20260603_upload_chunk.sql` (forma correta) é `CREATE IF NOT EXISTS` (pulso em banco existente) e **não está no manifest**. Sintoma: 400 amigável `UPLOAD_CHUNK_SCHEMA_MISSING` (catch 42703 em `UploadChunkService.StartAsync`).
  2. **Bug de código (pinned em `UploadChunkService.cs` L83-84):** `INSERT INTO ged.upload_batch_item (…) SELECT …` tem **13 colunas × 12 expressões** — falta o valor `@uploadId` para a coluna `upload_session_id` (o `@fileName` entra no slot errado). Com as duas colunas criadas manualmente no repro: `42601 "INSERT tem mais colunas alvo do que expressões"` → 500 genérico "Servidor". Ou seja: **mesmo com schema perfeito, o chunk flow falha hoje** — fix obrigatório do Bloco A.

## Instalação limpa / manifest
- 44 migrações no manifest executadas em ordem: **18 falham / 26 ok**.
- Tabelas existentes apenas no dump legado (sem migração): `protocolo_setor*`, núcleo de retention. Migrações de código que **não estão no manifest**: `20260603_upload_chunk.sql`, `2026_08_27_physical_archive_2.sql`, `2026_09_03_physical_archive_reg_status_compat_fix.sql` (há 96 arquivos de migração vs 44 listados).
- Regra adotada: append no fim do manifest + migrações incrementais guardadas; validar em instalação limpa + banco de atualização descartável; nunca migrar produção.

## Estado de dados do repro (útil p/ regressão)
- `protocol_request`=1 (P3 acima, ADJ_ANSWERED, 1 anexo); `physical_loan`=0; `loan_request` REQUESTED=2/OVERDUE=5; `retention_case`=8 OPEN; `upload_batch`≈16 (`c87e9bc9` COMPLETED 1/1; `0817a7ae` COMPLETED 0/2; `baab2760` OPEN); documentos 202+ (novo: `081bfa9a`); storage com `up-batch-a.txt` sob o tenant `0000…0001`.
- Alterações locais no repro (fora do repo): `ALTER ged.upload_session ADD COLUMN IF NOT EXISTS metadata_json/batch_item_id` (aplicado para expor o bug 42601).
