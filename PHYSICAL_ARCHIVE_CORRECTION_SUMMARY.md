# Validação da Correção — Physical Archive 2.0 Dashboard

## 1. Erro Confirmado

**SQLSTATE:** 42601 (Syntax error)  
**Origem:** `PhysicalArchive2Service.DashboardAsync()`, linha 34-43  
**Causa:** Escape de aspas duplas incorreto em raw string C#

```csharp
// ANTES (INCORRETO):
as \"Boxes\"

// DEPOIS (CORRETO):
as "Boxes"
```

Dentro de raw strings C# (prefixo `$"""`), as aspas duplas não são interpretadas como caracteres de escape. A barra permanecia literal no SQL enviado ao PostgreSQL, causando erro de sintaxe.

## 2. Correção Aplicada

Arquivo: `InovaGed.Infrastructure/PhysicalArchive2/PhysicalArchive2Service.cs`

**8 aliases corrigidos:**
- `\"Boxes\"` → `"Boxes"`
- `\"LabelledBoxes\"` → `"LabelledBoxes"`
- `\"UnlocatedBoxes\"` → `"UnlocatedBoxes"`
- `\"LoanedBoxes\"` → `"LoanedBoxes"`
- `\"OpenInventories\"` → `"OpenInventories"`
- `\"OverdueLoans\"` → `"OverdueLoans"`
- `\"MonthlyMovements\"` → `"MonthlyMovements"`
- `\"PendingChecks\"` → `"PendingChecks"`

**Preservado:**
- Parâmetro `@t` (tenant_id)
- Filtros por tenant `tenant_id=@t`
- Filtros de registros ativos `reg_status='A'`
- Predicados condicionais (labelledPred, unlocatedPred, etc.)
- Mapeamento de DashboardRow
- Comportamento de contadores zero para tenants vazios

## 3. Análise de Raw Strings

Verificação de todas as raw strings no módulo:

| Método | Linha | Status | Observação |
|--------|-------|--------|-----------|
| DashboardAsync | 34-43 | ✅ Corrigido | 8 aliases com escape removido |
| ScanAsync | 68-72 | ✅ OK | Sem aliases entre aspas, apenas lógica SQL |

**Conclusão:** Nenhuma outra query no módulo apresenta o mesmo problema de escape.

## 4. Validação SQL

### Antes (Gerador de Erro):
```sql
select ... as \"Boxes\",
       ... as \"LabelledBoxes\",
       ...
```
Quando enviado ao PostgreSQL, é interpretado como:
```
Error at position 89: invalid use of escape sequence
```

### Depois (Sintaxe Correta):
```sql
select ... as "Boxes",
       ... as "LabelledBoxes",
       ...
```
Identificadores com aspas duplas são válidos em PostgreSQL.

## 5. Contratos Afetados

- **Application:** `IPhysicalArchive2Service.DashboardAsync(Guid tenantId, CancellationToken ct)`
- **DTO:** `PhysicalArchiveDashboard(long Boxes, long LabelledBoxes, ...)`
- **Controller:** `PhysicalController.Index()` → rota GET /Physical/ e GET /Physical/Dashboard
- **View:** `Views/Physical/Index.cshtml` → renderiza os 8 indicadores KPI

Nenhuma alteração em contratos foi necessária. A correção é puramente de sintaxe SQL.

## 6. Próximas Etapas

- [ ] Executar teste de integração contra PostgreSQL real
- [ ] Validar que o dashboard carrega sem exceção
- [ ] Verificar valores dos indicadores com dados de teste
- [ ] Validar isolamento por tenant
- [ ] Verificar comportamento com tenant vazio
- [ ] Implementar testes de regressão (PhysicalArchive2DashboardSqlTests.cs criado)
- [ ] Evoluir dashboard com navegação para listagens filtradas
- [ ] Auditar outras jornadas do módulo

## Status

**Correção:** ✅ Concluída  
**Compilação:** Aguardando (ambiente com problemas de assets.json)  
**Testes:** Teste de integração criado, aguardando PostgreSQL  
**Entrega:** Bloqueada até validação com banco de dados real
