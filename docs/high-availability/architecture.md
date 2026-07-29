# Arquitetura de alta disponibilidade

`SingleNode` permanece o padrão e conserva storage e Data Protection locais. `MultiNode` registra identidades e heartbeats no PostgreSQL; dependências compartilhadas tornam-se obrigatórias quando `RequireDistributedDependencies=true`. `BlueGreen` adiciona duas cores e uma janela de compatibilidade, sem autorizar switch pela aplicação Web.

O plano de dados usa PostgreSQL para registro, deployments e leases com fencing token monotônico. Documentos permanentes e o key ring devem ser compartilhados. SignalR e cache distribuído usam Redis isolado por produto, ambiente e cluster. O plano de controle permanece na Deployment Tool/load balancer e nunca executa rollback automático do schema.

## Segurança operacional

Identidades não usam IP; o fallback combina hostname normalizado, PID e sufixo aleatório. Configurações versionadas contêm somente exemplos `.invalid`, nunca credenciais. `/health/live` é exclusivamente local ao processo; `/health/ready` inclui dependências; `/health/node` publica apenas identidade não sensível e versão abreviada.

## Implantação incremental

Esta entrega introduz os contratos, schema aditivo, identidade, heartbeat e lease/fencing. Backplane Redis, providers de load balancer, drain e homologação multi-nó permanecem explicitamente não homologados; a PR deve continuar draft.
