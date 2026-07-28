# Arquitetura de continuidade

A camada Application mantém contratos e DTOs. A Infrastructure contém repositórios separados de política, catálogo, portabilidade, recovery plan e offboarding; serviços separados calculam objetivos, retenção, integridade e exclusão bloqueada. `BackupOrchestrator` coordena banco, provider PostgreSQL e artefatos sem expor a connection string. Cada contrato é registrado diretamente em uma implementação produtiva.
