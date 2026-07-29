# Arquitetura de observabilidade 04.1.26

A camada **Application** define fontes canônicas, SLI/SLO, error budget, lifecycle de incidentes, alertas e runbooks. **Infrastructure** compõe OpenTelemetry vendor-neutral e sanitização. **Web** resolve correlação e métricas/log resumido por request. Telemetria de alto volume sai somente por OTLP explicitamente habilitado; PostgreSQL guarda apenas resumos operacionais.

O pipeline usa instrumentações ASP.NET Core, HttpClient e runtime, sampler parent-based e exportação em lote assíncrona do SDK. OTLP nasce desabilitado e endpoint vazio. O Collector de exemplo limita memória/lote e exporta apenas para `debug`; sua configuração deve ser substituída por operação aprovada.

A aplicação não depende do Collector para atender requests. Labels são limitados a rota-template, método e classe de status. Correlation ID é UUID, fica em log/trace, nunca em label.
