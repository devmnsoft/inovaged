# Plano de Correção e Evolução — Physical Archive 2.0

## Status Atual

### ✅ CONCLUÍDO — Bloco A: Corrigir Erro Confirmado

**Problema:** SQLSTATE 42601 em `PhysicalArchive2Service.DashboardAsync()`

**Causa:** Escape de aspas duplas incorreto em raw string C#
```csharp
// ANTES
as \"Boxes\"  // Barra fica literal no SQL

// DEPOIS
as "Boxes"    // Correto em raw string
```

**Correção Aplicada:**
- 8 aliases corrigidos: Boxes, LabelledBoxes, UnlocatedBoxes, LoanedBoxes, OpenInventories, OverdueLoans, MonthlyMovements, PendingChecks
- Commit: d161040 (fix: corrige escape de aspas em raw string do dashboard Physical Archive 2.0)
- Teste: PhysicalArchive2DashboardSqlTests.cs criado

**Status do Build:** Bloqueado por problema de environment (project.assets.json corrompido) — não é causado pela correção

---

## Próximas Etapas

### ⏳ BLOQUEADO — Fase 1: Validação com PostgreSQL Real

**Requisitos:**
- PostgreSQL 13+ com schema physical_archive 2.0
- Acesso ao banco de testes

**Testes de Validação:**
1. `DashboardAsync` com tenant vazio → espera 0 em todos os contadores
2. `DashboardAsync` com dados de teste → validar contadores
3. Isolamento por tenant → dados de outro tenant não aparecem
4. Ausência de SQLSTATE 42601 → query executa limpa
5. Rotas HTTP GET /Physical/ e /Physical/Dashboard → renderizam sem exceção
6. View apresenta 8 KPIs corretamente

**Status:** Aguardando disponibilidade de PostgreSQL

---

## Estrutura do Módulo Mapeada

### Tabelas do Schema `ged`
| Tabela | Colunas Principais | Propósito |
|--------|-------------------|-----------|
| `physical_location` | id, tenant_id, location_code, name, parent_id, is_active, reg_status | Hierarquia de localizações |
| `physical_box` | id, tenant_id, box_code, label_code, location_id, status, current_holder, reg_status | Caixas de arquivo |
| `physical_box_document` | id, tenant_id, box_id, document_id, linked_at, reg_status | Vínculo documento-caixa |
| `physical_movement` | id, tenant_id, box_id, from_location_id, to_location_id, performed_at, reason, reg_status | Movimentações com data |
| `physical_inventory_session` | id, tenant_id, session_number, location_id, status, started_at, closed_at, reg_status | Sessões de inventário |
| `physical_inventory_item` | id, tenant_id, session_id, box_id, result (PENDING/MISSING/WRONG_LOCATION/FOUND), scanned_code, found_location_id, reg_status | Conferências |
| `physical_loan` | id, tenant_id, loan_number, box_id, status (OPEN/RETURNED), due_at, requested_by_name, reg_status | Empréstimos |
| `physical_custody_event` | id, tenant_id, source_type, source_id, event_type, occurred_at, performed_by, reg_status | Cadeia de custódia |

### Jornadas Implementadas
**Queries (IPhysicalQueries):**
- ListLocationsAsync → busca com filtro por nome
- GetLocationAsync → edição de localização
- ListBoxesAsync → busca com filtro por código/nome
- GetBoxAsync → edição de caixa
- GetBoxContentsAsync → documentos em caixa
- GetBoxHistoryAsync → histórico de movimentações
- GetPhysicalMapAsync → mapa de guarda (localização × caixas)

**Commands (IPhysicalCommands):**
- UpsertLocationAsync/DeleteLocationAsync → CRUD de localizações
- SetLocationActiveAsync → ativar/inativar localização
- UpsertBoxAsync/DeleteBoxAsync → CRUD de caixas
- SetBoxStateAsync → alterar estado (ACTIVE, LOANED, etc.)
- AddDocumentToBoxAsync → vincular documento à caixa
- RemoveDocumentFromBoxAsync → desvincular documento
- MoveDocumentToBoxAsync → mover documento entre caixas

**PhysicalArchive2Service (Dashboard e Operações):**
- DashboardAsync → 8 indicadores (CORRIGIDO)
- BoxesAsync/LocationsAsync → dropdowns
- MovementsAsync → últimas 300 movimentações
- InventoriesAsync → sessões de inventário recentes
- LoansAsync → empréstimos com status
- CustodyAsync → cadeia de custódia por caixa
- StartInventoryAsync/ScanAsync/CloseInventoryAsync → fluxo de inventário
- MoveAsync → movimentação com transação e eventos
- LoanAsync/ReturnLoanAsync → empréstimos

### Controller (PhysicalController)
**Rotas Implementadas:**
- `GET /Physical/` ou `GET /Physical/Dashboard` → Dashboard com 8 KPIs
- `GET /Physical/Locations`, `POST /Physical/Locations/Save`, `POST /Physical/Locations/{id}/Delete`
- `GET /Physical/Boxes`, `POST /Physical/Boxes/Save`, `POST /Physical/Boxes/{id}/Delete`, `POST /Physical/Boxes/{id}/State`
- `GET /Physical/Inventory`, `GET /Physical/Inventory/{id}`, `POST /Physical/Inventory/Start`, `POST /Physical/Inventory/{id}/Scan`, `POST /Physical/Inventory/{id}/Close`
- `GET /Physical/Movements`, `POST /Physical/Movements/Create`
- `GET /Physical/Loans`, `POST /Physical/Loans/Create`, `POST /Physical/Loans/{id}/Return`
- `GET /Physical/Custody/{boxId}`
- `GET /Physical/BoxContents`, `GET /Physical/BoxHistory`, `GET /Physical/PhysicalMap`

### Views
- `Index.cshtml` → Dashboard com 8 cards KPI (navegação central)
- `Locations.cshtml`, `LocationForm.cshtml` → Gestão de localizações
- `Boxes.cshtml`, `BoxForm.cshtml` → Gestão de caixas
- `Inventory.cshtml`, `InventoryDetails.cshtml` → Fluxo de inventário
- `Movements.cshtml` → Histórico de movimentações
- `Loans.cshtml` → Empréstimos ativas/devolvidas
- `Custody.cshtml` → Cadeia de custódia
- `BoxContents.cshtml`, `BoxHistory.cshtml`, `PhysicalMap.cshtml` → Consultas especializadas

---

## Bloco B: Evoluir Dashboard e Navegação

### Versão Atual (Após Correção)
- 8 cards KPI com contadores
- Links genéricos para Caixas, Localizações, etc. (sem filtro)
- Sem pesquisa direta
- Sem navegação contextualizada

### Versão Evoluída (Próximo Incremento)

**Cards com Navegação Filtrada:**
1. **Caixas cadastradas** → GET /Physical/Boxes (sem filtro, pois é linha de base)
2. **Caixas com etiqueta** → GET /Physical/Boxes?labelCode=!empty
3. **Sem localização** → GET /Physical/Boxes?location=unlocated
4. **Em empréstimo** → GET /Physical/Loans (status=OPEN)
5. **Inventários abertos** → GET /Physical/Inventory (status=OPEN)
6. **Empréstimos vencidos** → GET /Physical/Loans (status=OVERDUE)
7. **Movimentações no mês** → GET /Physical/Movements (filter=thisMonth)
8. **Pendências de conferência** → GET /Physical/Inventory (result=PENDING)

**Melhorias em Listagens:**
- Pesquisa por código e nome
- Filtros por localização, estado, período
- Paginação (20, 50, 100 linhas)
- Ordenação estável (código, data, status)
- Indicador de filtros aplicados
- Botão "Limpar filtros"
- Estados: carregando, vazio, erro

**Melhorias em Experiência:**
- Preservar filtros ao voltar
- Mostrar ID apenas em tooltips/debug
- Labels amigáveis (ex: "Em empréstimo" em vez de "LOANED")
- Ajuda inline "Como usar"

---

## Bloco C: Concluir Jornadas Existentes

### 1️⃣ Caixas e Localização

**Audit Checklist:**
- [ ] Validação de `box_code` conforme gerador canônico
- [ ] Localização marcada como `reg_status='A'` e `is_active=true`
- [ ] Vínculo de documentos valida existência de caixa e documento
- [ ] Releitura após gravação (GET após POST/PUT)
- [ ] Inativação: `reg_status='D'` ou `is_active=false`? Consistência.
- [ ] Histórico em `physical_custody_event` para todas as alterações?

**Status Atual:**
- ✅ CRUD de caixas implementado (UpsertBoxAsync, DeleteBoxAsync)
- ✅ CRUD de localizações implementado
- ✅ Validação de campos no controller
- ⏳ **Verificar:** Histórico completo em events, fluxo de inativação

### 2️⃣ Movimentações

**Audit Checklist:**
- [ ] Origem, destino, responsável, data, justificativa: obrigatórios
- [ ] Validação de destino: localização ativa?
- [ ] Proteção contra concorrência: lock pessimista (FOR UPDATE)?
- [ ] Atualização de localização + evento na **mesma transação**?
- [ ] Idempotência: repetir mesma movimentação sem efeito duplicado?

**Status Atual:**
- ✅ MoveAsync implementado com transação explícita
- ✅ Insere em `physical_movement` e `physical_custody_event` atomicamente
- ⏳ **Verificar:** Implementação de retry/idempotência com correlation_id

### 3️⃣ Inventário

**Audit Checklist:**
- [ ] Abertura: escopo (toda a guarda, localização específica)?
- [ ] Conferência por código ou etiqueta (label_code)?
- [ ] Identificação: caixa ausente (UNEXPECTED), localização divergente (WRONG_LOCATION)?
- [ ] Código desconhecido: tratamento explícito (não silencioso)?
- [ ] Proteção contra duplicação: mesmo código×sessão?
- [ ] Fechamento: validação de estado (OPEN → CLOSED)?
- [ ] Relatório de divergências disponível?

**Status Atual:**
- ✅ StartInventoryAsync, ScanAsync, CloseInventoryAsync implementados
- ✅ Resultados: PENDING, UNEXPECTED, FOUND, WRONG_LOCATION
- ✅ InventoryDetails retorna detalhes completos
- ⏳ **Verificar:** Relatório de divergências, view de summário

### 4️⃣ Empréstimos

**Audit Checklist:**
- [ ] Solicitação: campos obrigatórios (requester, box_id)?
- [ ] Consulta (status): OPEN vs RETURNED?
- [ ] Devolução: validação de prazo (overdue_at < now())?
- [ ] Bloqueio: uma caixa não pode estar em múltiplos empréstimos OPEN?
- [ ] Histórico de quem pegou, quando, quem devolveu?
- [ ] Indicador de atraso: coerente com due_at?

**Status Atual:**
- ✅ LoanAsync, LoansAsync, ReturnLoanAsync implementados
- ✅ Status: OPEN, RETURNED
- ✅ Indicador de OVERDUE em LoansAsync (status='OPEN' and due_at<now())
- ⏳ **Verificar:** Proteção contra empréstimos concorrentes, histórico completo

---

## Bloco D: Design e Acessibilidade

### Verificação de Cascata CSS

**Arquivo:** `InovaGed.Web/css/physical-archive.css`

**Checklist:**
- [ ] Campos (text, select, textarea): contraste 4.5:1
- [ ] Placeholders: visível e contrastado
- [ ] Labels associados com `<label for="id">`
- [ ] Estados (focus, error, disabled, readonly): visuais e cores distintas
- [ ] Tabelas: sem cortes de ações, responsivo
- [ ] Modais: navegação por ESC, foco gerenciado
- [ ] Badges (status): símbolo + cor (não apenas cor)
- [ ] Zoom 200%: layout não quebrado

**Plano:** Revisar CSS atual e aplicar acessibilidade

---

## Bloco E: IA — Avanço Consultivo (Condicional)

**Decisão Prévia:** Infraestrutura de IA governada existe?

**Se SIM:**
- Resumo de divergências de inventário (leitura apenas)
- Resumo de empréstimos vencidos (leitura apenas)
- Indicação de caixas sem localização (leitura apenas)
- Explicação de resultados, mas NÃO cálculos nem criação de dados

**Se NÃO:**
- Implementar resumo determinístico (sem IA)
- Documentar próximo incremento como "Assistência de IA"

---

## Próximo Passo Crítico

**DESBLOQUEADOR:** Ambiente com PostgreSQL 13+

Assim que disponível, executar:
```bash
cd C:\MNSOFT\inovaged
dotnet restore
dotnet build
dotnet test --filter "Category=Integration AND Database=PostgreSQL" \
  --project InovaGed.Application.Tests/InovaGed.Application.Tests.csproj
```

**Resultado Esperado:**
- ✅ Dashboard retorna PhysicalArchiveDashboard sem exceção
- ✅ Valores corretos segundo predicados (0 para tenant vazio)
- ✅ Nenhum SQLSTATE 42601
- ✅ Isolamento por tenant verificado

**Após Validação:** Liberar para Bloco B (evolução de navegação)
