# Bloco A — Confiabilidade de upload/anexos de GED (correções + evidências antes/depois)

Data: 29/09/2026 · Ambiente: `inovaged_repro` (PG 18) · Build Release (0 erros; baseline 240 avisos → mantido) · App em `http://localhost:5000`.
Probes (curl + psql, corpos salvos): pasta `...\opencode\trnsito\verify\`; scripts em `...\opencode\trnsito\` (`probe_chunk_e2e.ps1`, `probe_chunk_2p.ps1`, `probe_blockA_verify.ps1`, `q_blocka_follow*.ps1`, `q_h3_details.ps1`, `q_h2_recalc.ps1`).

Legenda de status: **passou** · **parcial** (correção aplicada, efeito limitado ao escopo tratado; causa restante em outro bloco) · **n executado** (caminho não exercitado via E2E, com justificativa).

## 1. Upload em chunks (U5–U8) — status: **passou**

### Antes (reproduzido em `03-evidencia-reproducao.md`)
1. `POST /Ged/UploadChunk/Start` → **500** genérico: `SQLSTATE 42601` — segundo INSERT do `StartAsync` (`upload_batch_item`) listava 13 colunas mas o SELECT tinha 12 expressões (faltava o valor `@uploadId`).
2. Com a migration de chunks ausente do manifest, `Start` → **400 amigável** `UPLOAD_CHUNK_SCHEMA_MISSING` (colunas `metadata_json`/`batch_item_id` inexistentes na tabela consolidada).

### Alterações
- `InovaGed.Infrastructure/Ged/Documents/UploadChunkService.cs` — `StartAsync`: INSERT alinhado 13/13 (inclui `@uploadId`).
- `database/migrations/2026_09_29_ged_upload_chunk_compatibility.sql` (**nova**) — `ALTER TABLE ged.upload_session ADD COLUMN IF NOT EXISTS batch_item_id/metadata_json` + índice parcial. Idempotente/aditiva.
- `database/migrations.manifest.json` — anexados os itens `20260603_upload_chunk` (arquivo já existia, fora do manifest; 100% idempotente) e a compatibilidade acima, na ordem correta.

### Depois (evidência executada hoje)
| Etapa | Resultado |
|---|---|
| `Start` (arquivo 2.054 B) | `200 {"success":true,"uploadId":"5ba8571d…","correlationId":"0HNOU6IOA5IJ9…"}` |
| Partes extras além do total | `400 {"errorStep":"MISSING_CHUNKS"/"VALIDATION","canRetry":true}` (validação correta) |
| Fluxo real **3 partes** (3.000 B, chunk 1.024) | `PART0/PART1/PART2` = 200; `COMPLETE` = 200 `status=COMPLETED` |
| `ged.upload_session` | `COMPLETED \| 3/3 \| document_id=c0b43c4e…` |
| `ged.upload_session_chunk` | índices 0,1,2 com tamanhos 1024/1024/952 |
| Documento/versão | `DOC-20260929142409-…` criado; `storage_path` coerente |
| Storage em disco | `…\c0b43c4e…\up-chunk-2p.txt` = 3.000 bytes, conteúdo íntegro (3.000 'A') |

Observação: sessão de chunk sem contexto de lote não cria linha em `upload_batch_item` (INSERT guardado por `WHERE @batchId IS NOT NULL`); o `batch_item_id` na sessão é referência macia sem FK. Comportamento pré-existente, não alterado por esta correção.

## 2. Falso sucesso no Finish do lote (U4) — status: **passou**

**Antes:** `Finish` do lote `0817a7ae` (2 arquivos, todos rejeitados) → `success:true`, `COMPLETED total=2 success=0 failed=0`.
**Depois:** `POST /Ged/UploadBatch/Finish` (mesmo lote) →
`{"success":false,"status":{…total:2,success:0,…},"message":"Lote finalizado sem nenhum documento criado. Revise os erros por arquivo e utilize Reenviar falhas."}`
Regra aplicada em `UploadBatchController.Finish`: `zeroSuccess = Total>0 && Success==0 && Pending==0` → `success=false` com mensagem explícita.

## 3. Numeração de protocolo (A2-1) — status: **passou**

**Antes:** INSERT em `ged.code_sequence` usava colunas inexistentes (`module/year/…`) → `SQLSTATE 42703` → 500 ao abrir protocolo.
**Depois:** `ProtocolRequestService.GenerateProtocolNoAsync` usa o formato real da tabela, com `SELECT … FOR UPDATE` + branch insert/update na mesma transação (tabela tem só PK; sem unique em `(tenant, entity)`):
- Criações E2E: `PROT-2026-000001` → `PROT-2026-000002` → `PROT-2026-000003` (sequencial, sem gap).
- Linha criada: `entity_name=PROTOCOL-2026 | prefix=PROT | current_value=3 | padding=6`.

## 4. Anexos em protocolo/ajuste (A2-2, A2-3, R4) — status: **passou** (falha por arquivo = n executado, ver §7)

**Antes:** nomes ilegais gravados como estão / falha pós-commit vira 500 ou "sucesso" falso; transição repetida `RespondAdjustment` criava **linhas duplicadas** de histórico (`ADJUSTMENT_ANSWERED→ADJUSTMENT_ANSWERED`, 8 linhas antes de hoje).

**Depois:**
- Helper compartilhado novo `InovaGed.Web/Common/ProtocolAttachmentSaver.cs`: sanitização de nome, try/catch por arquivo, honra o resultado de `AddAttachmentAsync`, compensa órfão via `IFileStorage.DeleteIfExistsAsync`, detalhes só em log. Erros por arquivo expostos como `TempData["FileErrs"]` (tabela nome/etapa/motivo/retry) nas views `ProtocolRequests/New` e `Protocols/Details`.
- `ProtocolRequestsController.New` POST: `vm ??= new` (mata o NRE A2-3), pré-filtro de vazios, mensagem segura no catch.
- `ProtocolsController.RespondAdjustment`: validação prévia antes da transição irreversível; anexos só após transição ok.
- `ProtocolRequestService.AssignOrTransitionAsync`: lock `for update` por linha; auto-transição (mesmo estado) = **no-op idempotente** (rollback + log, sem UPDATE nem linha de histórico).

Evidência executada:

| Cenário | Resultado |
|---|---|
| POST `/ProtocolRequests/New` c/ anexo `a\|b<c>.txt` | 302 → protocolo `6db777e2…`; anexo gravado como **`a_b_c_.txt`**, arquivo existe em storage |
| POST New c/ `x:y*z?.txt` | 302 → `7fb53845…` `PROT-2026-000003`; anexo **`y_z_.txt`**; página seguinte exibe alerta **"Protocolo aberto com sucesso."** e lista o anexo |
| `RespondAdjustment` no P3 ×4 (já em `ADJUSTMENT_ANSWERED`) | 302 a cada um; alerta "Ajuste respondido."; histórico **8 → 8** (sem duplicar); status inalterado |

n executado: caminho de **falha de gravação por arquivo** (tabela FileErrs) — nenhum gatilho determinístico encontrado no fluxo E2E atual (falha simulada exigiria storage indisponível). Caminho coberto por código (try/catch + compensação) e pelo mesmo `catch` que produz mensagens seguras demonstradas em H2/H4/H3.

## 5. Mensagens seguras + vazamento em HTML (H2) — status: **parcial**

**Antes:** `catch` renderizava `ex.Message` — o texto `retention_case.id`/`23502` aparecia na tela (H2).
**Depois:** `TemporalidadeController` (Recalculate/GenerateNow/CreateTerm), `RetentionController.Recalculate`, `RetentionCaseController` (Create/DecideItem/Close/Execute) e `RetentionDestinationController` (Create/Execute) trocaram `ex.Message` por mensagens estáveis ("Não foi possível … Tente novamente ou contate o suporte"); detalhes completos somente no log.
- E2E: após Recalcular falhar, a página mostrou a mensagem segura; `retention_case.id`/`23502` **não** aparecem mais no HTML (`leak=False`).
- O log contém a causa real: `Npgsql.PostgresException 23502: o valor nulo na coluna "id" da relação "retention_case"` — é o bug de SQL do **Bloco C** (`createCaseSql` sem `id`/`title`, colunas desconhecidas no `createItemSql`). Recalcular continua falhando internamente (contagem estável: 8 casos / 8 itens, sem dados corrompidos).

## 6. Permissões específicas nos POST de temporalidade (H4/H4B) — status: **passou**

**Antes:** usuário somente-leitura (HOSPITAL) executava POSTs via URL direta (aceitos sem política específica).
**Depois:** nova política `AppPolicies.RetentionManage` (registrada em `Program.cs` com `RequireAny(Admin, AdministradorOphir, ArquivistaOphir)` + IsFullAdmin). Aplicada aos POSTs de: `Temporalidade` (Recalculate/GenerateNow/CreateTerm), `Retention/Recalculate`, `RetentionCases` (Create/DecideItem/Close/Execute), `Retention/Destination` (Create/Execute). GETs seguem `[Authorize]`/políticas existentes.

Evidência (usuário `hospital`, logado, URL direta):

| POST | Antes | Depois |
|---|---|---|
| `/Temporalidade/Recalculate` | aceito | `302 → /Account/AccessDenied?ReturnUrl=%2FTemporalidade%2FRecalculate` |
| `/Temporalidade/GenerateNow` | aceito | `302 → AccessDenied` |
| `/Retention/Destination/Create` | aceito | `302 → AccessDenied` |
| `/RetentionCases/Close` | aceito | `302 → AccessDenied` |

- Admin no mesmo POST → permitido (`302 → /Temporalidade`), GETs seguem liberados (`GET /Temporalidade` = 200 para hospital).
- Auditoria: 5 registros novos em `ged.security_access_failure_log` (`happened_at`) no período — políticas negadas são auditadas (RNF02 parcial).

## 7. View ausente `RetentionDestination/Details` (H3) — status: **passou**

**Antes:** GET `Details` → 500 (view inexistente; só `Index.cshtml` existia).
**Depois:** `Views/RetentionDestination/Details.cshtml` criada (itens do lote, classificação/prazos/HOLD, botões CSV + Executar com antiforgery e confirmação, nota de que a execução não elimina fisicamente sem autorização).
E2E: `GET /Retention/Destination` = 200; `GET /Retention/Destination/Details?batchId=5f45a363…` = **200**, com cabeçalho de itens, botão "Executar lote" e token antiforgery presentes.

## 8. Arquivos alterados (Bloco A)

Código:
- `InovaGed.Infrastructure/Ged/Documents/UploadChunkService.cs`
- `InovaGed.Infrastructure/Ged/Protocols/ProtocolRequestService.cs`
- `InovaGed.Web/Common/ProtocolAttachmentSaver.cs` (novo)
- `InovaGed.Web/Controller/ProtocolRequestsController.cs`
- `InovaGed.Web/Controller/ProtocolsController.cs`
- `InovaGed.Web/Controller/UploadBatchController.cs`
- `InovaGed.Web/Controller/TemporalidadeController.cs`
- `InovaGed.Web/Controller/RetentionController.cs`
- `InovaGed.Web/Controller/RetentionCaseController.cs`
- `InovaGed.Web/Controller/RetentionDestinationController.cs`
- `InovaGed.Web/Models/Security/AppPolicies.cs`
- `InovaGed.Web/Program.cs` (registro da política `RetentionManage`)
- `InovaGed.Web/Views/ProtocolRequests/New.cshtml`
- `InovaGed.Web/Views/Protocols/Details.cshtml`
- `InovaGed.Web/Views/RetentionDestination/Details.cshtml` (nova)

Banco:
- `database/migrations/2026_09_29_ged_upload_chunk_compatibility.sql` (nova; aditiva/idempotente)
- `database/migrations.manifest.json` (+ `20260603_upload_chunk`, + compatibilidade)

## 9. Pendências / riscos do Bloco A
1. **Recalcular temporalidade ainda falha internamente** (23502 `retention_case.id`) — correção no SQL é Bloco C; enquanto isso a UI mostra mensagem segura e nada é corrompido.
2. Tabela de erro **por arquivo** (FileErrs) não exercitada via E2E (sem gatilho determinístico); verificar em teste com storage indisponível no próximo ciclo.
3. Porta antiga `/Protocols/Details/{id}` responde 405 (rótulo de rota pré-existente `{id:guid}`); o redirect atual aponta para a rota válida — manter vigilância se links antigos forem referenciados.
4. Nada publicado/mergeado; tudo em working tree da branch `main` (commit pendente).
