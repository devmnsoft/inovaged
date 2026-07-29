# Inventário de componentes com estado

Inventário obtido com `rg` antes das alterações. A classificação indica o contrato para execução em cluster.

| Componente | Estado | Classificação | Decisão |
|---|---|---|---|
| Caches de dashboard, permissões, pesquisa, analytics e controllers (`IMemoryCache`) | Cache derivável | NodeLocalAllowed | Manter local; chaves compartilhadas futuras devem usar cache distribuído versionado. |
| `PreviewQueue` e `PreviewWorker` | Fila em processo | UnsafeForCluster | Não usar como garantia entre nós; migrar enqueue/claim para PostgreSQL antes de habilitar preview distribuído. |
| `PreviewLocks` | semáforos estáticos | MustUseDatabaseLease | Lock local não evita finalização concorrente; usar lease/idempotência no banco. |
| `UploadConcurrencyLimiter` | contadores/lock locais | NodeLocalAllowed | Proteção de capacidade por nó; limites globais exigem claim/lease no banco. |
| `OcrAutoSchedulerWorker`, `DocumentQualitySchedulerWorker`, `RetentionDailyWorker`, `LoanOverdueWorker` | timers/schedulers | MustBeLeaderOnly | Executar sob lease específico por responsabilidade. |
| OCR, GED processing e stale upload workers | consumidores | MustUseDatabaseLease | Múltiplas instâncias somente com claim atômico, retry e idempotência. |
| `PostgresJobExecutionLock` e advisory locks OCR/códigos | lock PostgreSQL | MustUseDatabaseLease | Preservar; evoluir tarefas longas para lease com expiração e fencing token. |
| `LocalFileStorage` | documentos permanentes locais | MustUseSharedStorage | SingleNode compatível; proibido em MultiNode/BlueGreen. |
| temporários de preview/OCR/upload | arquivos locais | NodeLocalAllowed | Permitidos quando retomada/finalização permanece no proprietário; preferir storage compartilhado. |
| Data Protection local | key ring | MustBeDistributed | MultiNode requer filesystem compartilhado ou Redis e `ApplicationName` igual. |
| SignalR OCR | barramento em processo | MustBeDistributed | MultiNode requer backplane Redis com prefixo produto/ambiente/cluster. |
| `SchemaCompatibilityState` | singleton mutável | NodeLocalAllowed | Estado de readiness do processo, recriável por nó. |
| Signing Agent dictionaries/replay lock | estado de agente desktop | NodeLocalAllowed | Fora do cluster Web; escopo local é deliberado. |

## Riscos bloqueadores ainda abertos

SignalR Redis, storage compartilhado efetivo, filas de preview, liderança aplicada a cada scheduler e testes reais PostgreSQL/Redis são gates de homologação. MultiNode não deve ser promovido enquanto estes itens não estiverem verdes.
