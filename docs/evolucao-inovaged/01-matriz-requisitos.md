# Matriz de Requisitos — Evolução InovaGED (Fase 1)

Data: 2026-09-29 · Base do código: `main` @ `c03d383` (árvore limpa, build Release 0 erros) · Banco de referência/repro: PostgreSQL 18, `inovaged_repro` (cópia de template do dev `postgres`).

Escopo da matriz: RF02–RF06, RN01–RN03, RN05, RNF01, RNF02. **RF01 (login) excluído por instrução.**

Legenda de status:
- **Implementado** — requisito atendido e verificado com evidência.
- **Parcial** — há capacidade relevante, mas falta restrição/campo/tela exigida.
- **Ausente** — não existe capacidade correspondente no código nem no banco.
- **Quebrado** — existe capacidade mas está defeituosa em cenário normal de uso.
- **Não verificado** — não foi possível executar teste conclusivo no ambiente.

---

## Funções

### RF02 — CRUD de Setores com inativação lógica
**Status: Parcial**

- Existe: `InovaGed.Web/Controller/ProtocoloCadastrosController.cs` expõe CRUD genérico sobre `ged.protocolo_setor` (tipo padrão `"setores"`): `Index/Novo/Editar/Salvar/Excluir` + cadastro usuário↔setor (`protocolo_usuario_setor`). "Excluir" é lógico: `reg_status='E', ativo=false`. Seleção por nome existe (`ProtocoloController.GetSetoresSelectAsync`, filtro `reg_status='A' and ativo=true`). Política: `AppPolicies.ProtocolManage` + `PodeAdministrar()` (FullAdmin/Gestor/Arquivista).
- Falhas/gaps:
  1. **Nenhuma migração cria `protocolo_setor`/`protocolo_setor_participante`/`protocolo_usuario_setor`** (busca em `database/**.sql` sem resultados; o módulo só funciona em bancos oriundos do dump legado). Instalação limpa pelo manifest → páginas do Protocolo com erro de tabela inexistente → **quebrado para novo ambiente**.
  2. Os 5 setores-alvo do processo DAME (UAI, Ambulatório, Radioterapia, Quimioterapia, Admissão de Pacientes) **não existem** no tenant de referência. Setores atuais (6): Arquivo Central, Diretoria Administrativa, Diretoria Adm e Financeira, Gabinete, Protocolo, Protocolo Geral.
  3. Não há proteção contra inativação/exclusão de setor com histórico (junts são `left join`; sem verificação de uso antes de virar `'E'` — hoje não quebra, mas apaga o setor das telas sem trilha).
- Cenários de teste: criar setor UAI; inativar e confirmar que seletores deixam listá-lo; inativar setor com protocolos existentes → sem erro 500; excluir setor com histórico → confirmar restrição ou aviso.

### RF03 — Registro de saída (check-out) DAME → Setor
**Status: Parcial**

- Existe (duas bases candidatas):
  1. `PhysicalArchive2Service.LoanAsync` (`InovaGed.Infrastructure/PhysicalArchive2/PhysicalArchive2Service.cs` L57) + `PhysicalController.CreateLoan` (`/Physical/Loans/Create`, política `FullAdminOnly`): empréstimo por **caixa**, com `requester` (texto livre), `department` (texto livre opcional), `dueAt`, evento de custódia `PHYSICAL_LOAN_CREATED`. A tabela `ged.physical_loan` já tem coluna `document_id` **não utilizada pelo serviço**.
  2. Módulo `loan_request`/`loan_request_item` (empréstimo de documentos) já carrega `is_physical`, `sector_id`, `reference_code`, `box_code`, `physical_location`, `is_manual`, `medical_record_number` (evidência: schema + 3 itens existentes no repro).
- Gaps contra o processo: destino obrigatório **do dicionário de setores** (RN05) ausente (campo livre opcional); portador com nome **ou matrícula** (RN03) ausente; vínculo a prontuários do catálogo GED não exigido; sem cautela imprimível; política única `FullAdminOnly` (sem matriz por perfil/setor — B6).
- Cenários: check-out de 1 prontuário com setor + portador → registro com usuário/data/hora; sem portador → validação server-side com mensagem específica; cross-tenant e cross-setor (B6).

### RF04 — Registro de retorno (check-in) ao DAME, com devolução parcial
**Status: Parcial**

- Existe: `PhysicalArchive2Service.ReturnLoanAsync` (L58, caixa), fluxo de retorno do módulo de empréstimos, e transição administrativa `RespondAdjustment` (protocol_request). Nenhuma dessas suporta **devolução parcial de um lote multi-itens** (não há header de lote).
- Cenário: lote com N itens; devolver k<N → k fechados, N-k segue ABERTO; cautela/histórico refletem o parcial.

### RF05 — Expedição em lote com portador vinculado
**Status: Ausente**

- `upload_batch*` trata upload de arquivos digitais (não é expedição física). `loan_request_item` permite vários itens por solicitação, mas sem registro único de expedição vinculando **portador** ao lote e sem limite/experiência de ≤100 prontuários. `ged.protocol_tramitation`/`protocolo_tramitacao` são encaminhamento administrativo de protocolos (legado), não custódia física.
- Cenário: 1 lote de até 100 prontuários + 1 portador → 1 número de lote, atomicidade (tudo ou nada), trilha por item.

### RF06 — Histórico cronológico por prontuário
**Status: Parcial**

- Existe: `PhysicalArchive2Service.CustodyAsync` (timeline por **caixa**), `BoxHistory`/`GetBoxHistoryAsync` (histórico físico de caixa), `protocol_request_history` (histórico por solicitação administrativa; guarda `user_name`, não id), `label_custody_event` (etiquetas).
- Faltam: tela/unificação **por prontuário (documento)** agregando movimentos físicos + empréstimos + protocolos administrativos, com filtros "prontuários retidos por setor", tempo excedido e pendências (B5).
- Cenário: prontuário com 2 ciclos de saída/devolução → linha do tempo cronológica completa, sem IDs expostos.

---

## Regras de negócio

### RN01 — Sem segunda movimentação aberta sobre o mesmo prontuário
**Status: Quebrado**

- `LoanAsync` (L57) faz `update ged.physical_box set status='LOANED'` **sem verificar empréstimo OPEN prévio, sem trava de concorrência** (nem `for update` em `LoanAsync`, diferente de `MoveAsync`) e **sem constraint exclusivo** impedindo duas linhas OPEN para a mesma caixa/documento (`2026_08_27_physical_archive_2.sql` só indexa código/caixa/documento, não status). Submissão dupla pode criar 2 saídas abertas.
- Evidência correlata no lado administrativo: sem guarda de status/idempotência, observadas **linhas duplicadas de auto-transição** em `protocol_request_history` (P3 `7a232c4b…`, ADJ_ANSWERED→ADJ_ANSWERED duplicado).
- Cenários: POST duplo sequencial e concorrente de check-out → exatamente 1 linha OPEN (DB + serviço); retry não duplica.

### RN02 — Soft delete (proibida exclusão física em Setores/Usuarios/Prontuários)
**Status: Parcial**

- Padrão `reg_status char(1)` ubíquo (‘A’ ativo; ‘E’/‘I’ inativo) + `deleted_at` em algumas tabelas (ex.: `loan_request` tem `deleted_at/deleted_by/delete_reason`). Verificações: `ProtocoloCadastrosController.Excluir` → lógico; `PhysicalCommands.DeleteBoxAsync` → `reg_status='I'` + trava de uso (batch_item) + auditoria.
- Gaps: nomenclatura não unificada (`deleted_at` vs `reg_status`); cobertura completa por entidade **não verificada** (há 96 arquivos de migração, 44 no manifest); `protocolo_setor.Excluir` não impede exclusão com histórico (ver RF02.3).
- Cenários: para cada entidade crítica, executar o delete da tela → confirmar atualização (não `DELETE`) e permanência consultável em modo de auditoria.

### RN03 — Portador obrigatório (nome ou matrícula) nos lotes
**Status: Ausente**

- Nenhum campo de portador em `physical_loan` (tem `requested_by_name` = solicitante, não transportador) nem nos fluxos de empréstimo; nada exige matrícula.
- Cenário: salvar lote sem portador → erro específico; com só matrícula → aceita.

### RN05 — Classificação de destino obrigatória entre os 5 setores
**Status: Ausente**

- `physical_loan.requested_by_department` é texto livre opcional; sem FK/obrigatoriedade ao dicionário de setores; sem relatório "prontuários retidos por setor".
- Cenário: check-out sem setor → bloqueio server-side; relatório agrupa por setor os itens ABERTOS.

---

## Requisitos não funcionais

### RNF01 — Lote de até 100 prontuários registrado em ≤ 2 s
**Status: Não verificado**

- Capacidade ainda ausente (RF05). Após implementação: medir com cronômetro em torno do POST de lote + confirmação no banco (médiana de 5 execuções), ambiente dev; alegação só com medida publicada na entrega.

### RNF02 — Toda gravação registra ID do usuário autenticado
**Status: Parcial**

- Presente (uuid): `physical_loan.loaned_by/returned_by`, `physical_movement.performed_by`, `physical_custody_event.performed_by`, `created_by` em várias tabelas; `IAuditWriter` usado em comandos físicos.
- Ausente/inconsistente: `protocol_request_history.user_name` (texto, sem id do usuário); alguns caminhos de upload/anexos guardam apenas `user_id` em sessão (ok) mas mensagens de erro expõem detalhes técnicos sem contexto de auditoria amigável.
- Cenário: sample aleatório de escritas de cada bloco → coluna de usuário populada; tela de auditoria localizável por usuário.
