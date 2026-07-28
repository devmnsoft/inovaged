# Matriz de testes de regressão

| Área | Evidência esperada |
| --- | --- |
| Solution | Projetos únicos e sem conteúdo após `EndGlobal`. |
| Dependências | Npgsql 8.x compatível com EF Core PostgreSQL 8.x; Domain sem banco. |
| Permissões | LEGACY preserva, AUDIT_ONLY audita, ENFORCED usa consulta real tenant-scoped. |
| Administração | Tabelas canônicas e erro SQL não vira zero silencioso. |
| Continuidade | Jobs com claim/lease; backup desabilitado por padrão. |
| Portabilidade | Idempotency-Key retorna export existente e verificador bloqueia traversal. |
| Regressão GED | Login, upload, OCR, preview, protocolo, empréstimos e menus devem ser conferidos em homologação. |
