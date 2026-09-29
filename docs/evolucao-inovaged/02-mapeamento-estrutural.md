# Mapeamento Estrutural — Decisão (Fase 1)

Data: 2026-09-29 · Pergunta respondida: onde cada conceito do processo DAME vive — `protocolo*` (legado) vs `protocol_request*` vs empréstimos (`loan_request*`) vs movimentação física (`physical_*`) — e qual estrutura o Bloco B estende.

## Inventário das quatro pilhas (com evidência)

| Pilha | Tabelas (schema `ged`) | Código principal | Uso real |
|---|---|---|---|
| **Protocolo administrativo (legado)** | `protocolo`, `protocolo_documento`, `protocolo_tipo(_documento)`, `protocolo_assunto`, `protocolo_prioridade`, `protocolo_canal_entrada`, `protocolo_motivo_arquivamento`, `protocol_tramitation`/`protocolo_tramitacao`, **`protocolo_setor`**, `protocolo_setor_participante`, `protocolo_usuario_setor` | `ProtocoloController`, `ProtocoloMelhoriasController`, `ProtocoloUsuariosSetorAvancadoController`, `ProtocoloCadastrosController` | Abertura/tramitação administrativa de protocolos entre setores; **dicionário de setores e permissões usuário↔setor** |
| **Solicitação/fila de trabalho** | `protocol_request`, `protocol_request_item` (`reference_code`, `box_code`, `physical_location`), `protocol_request_history` | `ProtocolRequestsController`, `ProtocolsController`, `ProtocolRequestService` | Fila de solicitação de prontuário com estados (REQUESTED→…→FINISHED/CANCELLED) e anexos |
| **Empréstimo de documentos** | `loan_request` (`is_physical`, `sector_id`, `delivery_mode`…), `loan_request_item` (`is_manual`, `reference_code`, `box_code`, `physical_location`, `medical_record_number`), `loan_history`, `loan_sla_policy`… | módulo Loans (`/Loans`) | Empréstimo digital/físico de documentos com SLA e aprovação |
| **Acervo físico v2 (PhysicalArchive2)** | `physical_location`, `physical_box`, `physical_box_document`, `physical_movement`, `physical_inventory_session/_item`, **`physical_loan`** (`box_id`, `document_id`, `status OPEN/RETURNED`, `due_at`), **`physical_custody_event`** (timeline de custódia) | `PhysicalController` (`/Physical/*`, política `FullAdminOnly`) + `PhysicalArchive2Service` | Caixas, localizações, inventário, movimentações, empréstimos de caixa e **cadeia de custódia** |
| *(Acervo físico v1)* | `box`, `batch_item`, `physical_location_history`, `box_content_history`, `box_location_history` | `PhysicalQueries`/`PhysicalCommands` (mesmo `PhysicalController`) | Telas Caixas/Localizações/Mapa físico usam esta pilha (mistura v1+v2 num mesmo controlador) |

### Descobertas de base (bloqueantes p/ instalação limpa)
1. **`protocolo_setor*` não tem migração nenhuma** — existe apenas em bancos construídos do dump legado. (Busca `CREATE TABLE … protocolo_setor` em `database/**/*.sql`: zero.)
2. **`2026_08_27_physical_archive_2.sql` e `2026_09_03_physical_archive_reg_status_compat_fix.sql` NÃO estão no `migrations.manifest.json`** → tabelas `physical_*` ausentes em instalação limpa, embora toda a UI `/Physical/*` exista.
3. `20260603_upload_chunk.sql` (correto, com `metadata_json`) também fora do manifest; forma aplicada pelo consolidado diverge (faltam `metadata_json` e `batch_item_id` em `upload_session`).
4. Schema de temporalidade/retention existe **só no dump** (Bloco C cria o núcleo faltante por migração incremental).
5. Instalação limpa executada: 44 migrações do manifest → 18 falham (ordem/dependências), 26 ok.

## Decisão de mapeamento (Bloco B)

- **B1 — Setores:** reutilizar `protocolo_setor` + `ProtocoloCadastrosController` (CRUD já existe, com soft delete e permissões). Migração incremental: criar as três tabelas `protocolo_setor*` quando ausentes (forma observada no repro) e **semeiar os 5 setores-alvo por tenant existente que os não tenha** (UAI, Ambulatório, Radioterapia, Quimioterapia, Admissão de Pacientes). Inativação = `ativo=false`+`reg_status` (já implementado); adicionar trava de "setor com histórico" na exclusão.
- **B2/B3 — Check-out/check-in de prontuários:** estender o núcleo `physical_*` (v2):
  - nova tabela **`physical_loan_batch`** (header de lote: `tenant_id`, `lot_number`, `sector_id` → `protocolo_setor`, `carrier_name`, `carrier_matricula`, `created_by/at`, `due_at`, `status`, contadores) — resolve RF05 (registro único de expedição) e RN03/RN05 (portador + setor obrigatórios por FK);
  - colunas em `physical_loan`: `batch_id`, `sector_id`, `carrier_*`, `movement_type` (SAIDA/DEVOLUCAO_PARCIAL…) via `ALTER TABLE … ADD COLUMN IF NOT EXISTS`;
  - cada prontuário do lote = 1 linha `physical_loan` (com `document_id`) + eventos `physical_custody_event` (`PHYSICAL_LOAN_CREATED`/`…_RETURNED`/`LOST`) → **devolução parcial** vira "fechar k linhas do lote";
  - reuso da identificação física/manual existente: `reference_code`/`box_code`/`physical_location` (mesma semântica já usada em `protocol_request_item`/`loan_request_item`) e catálogo GED para resolver prontuário por código.
- **B4 — Sem segunda saída aberta:** constraint exclusivo parcial (`unique (tenant_id, document_id) where status='OPEN' and reg_status='A'`) **+** guarda no serviço (SELECT … FOR UPDATE no documento/caixa antes de abrir) — os dois níveis pedidos; submissão dupla segura por constraint.
- **B5 — Histórico:** fonte = `physical_custody_event` (+ `physical_movement`, `physical_loan`), tela por prontuário unificando tudo, com filtros retidos-por-setor/tempo/pendência.
- **B7 — Página Protocolo:** redesenho da UI do módulo `protocol_request*` mantendo URLs existentes (`/ProtocolRequests/*`, `/Protocols/*`) — pilha administrativa permanece intacta.
- **`ged.protocolo*` (legado):** tratado como dicionário/histórico administrativo; não é a pilha de custódia (confirma decisão anterior: `protocol_tramitation` é encaminhamento administrativo).
- **Empréstimos (`loan_request*`):** permanecem como empréstimo de documento; o novo fluxo DAME↔setor **não** depende deles (evita acoplamento a SLA/aprovação digital) e coexiste.

## Migrações (incrementais, append no fim do manifest, idempotentes)
1. `physical_*` v2: reanexar os dois arquivos existentes ao manifest (não reescrever) ou publicar equivalente `create table if not exists` guardado — decisão: **publicar migração incremental guardada** (forma idêntica à dos arquivos existentes) + anotar os arquivos originais no manifest.
2. `protocolo_setor*` (criar se ausentes) + seed dos 5 setores por tenant.
3. Bloco B: `physical_loan_batch` + colunas novas em `physical_loan` + constraint exclusivo de OPEN.
4. Bloco A: `upload_session.metadata_json`/`batch_item_id`; anexar `20260603_upload_chunk.sql` ao manifest.
5. Bloco C: núcleo de retention ausente (forma compatível com o dump) + índices.

Regra geral: **nunca reescrever migração aplicada**; sempre `IF NOT EXISTS`/guardas; validar em instalação limpa e em banco de atualização descartável.
