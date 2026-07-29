# Inventário atual de observabilidade

Inventário executado em 2026-07-29 no SHA inicial `6eaef7dd7bc023df37cd9e3df0047672ed473d14` com o comando solicitado `rg -n "ILogger|Console\.Write|ActivitySource|Activity\.|Meter\(|Counter<|Histogram<|ObservableGauge|TraceIdentifier|CorrelationId|Stopwatch|AddHealthChecks" . --glob '*.cs'` (757 ocorrências).

| Área | Evidência representativa | Classificação | Tratamento |
|---|---|---|---|
| Serviços e repositories | `ILogger<T>` e templates estruturados | StructuredLog | Preservar; sanitizar propriedades |
| Auditoria | `IAuditWriter`, eventos de autenticação e documentos | AuditEvent | Preservar fora do sampling |
| OCR/preview/search | `Stopwatch` e correlation ad hoc | TraceCandidate, MetricCandidate | Fontes canônicas e histogramas |
| ASP.NET | health checks, middlewares, `TraceIdentifier` | HealthSignal, TraceCandidate | correlação W3C + resumo de request |
| Cluster/deployment/continuity | heartbeats, leases, logs de operação | HealthSignal, IncidentSignal | métricas agregadas e markers |
| Ferramentas CLI | `Console.WriteLine/Error` para saída de comando | StructuredLog | Permitido como contrato CLI, não telemetria Web |
| Logs com query, IDs de documento/usuário | templates encontrados em search/guardian | UnsafeTelemetry | backlog: remover valor bruto e aplicar sanitizer |
| Cronômetros e logs duplicando duração | workers e pipelines | DuplicateTelemetry | convergir para span + histograma + único log final |

## Riscos encontrados

Há logs legados com query e identificadores de documento/usuário. Esta evolução introduz a fronteira de sanitização e proíbe esses campos em novas métricas; a conversão integral dos 757 pontos permanece incremental para evitar regressão funcional. Nenhum dado foi enviado externamente durante o inventário.
