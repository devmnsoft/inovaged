# Relatório parcial — evolução 04.1.18

## Entregue

- correção estrutural do CS1503 por meio de identificador `Guid` tipado;
- comando de auditoria tipado e overload legado obsoleto;
- correlation ID em coluna e JSON, event type, outcome e reason code explícitos;
- serviço central de auditoria de autenticação e códigos estáveis;
- migration aditiva da estrutura de sessões;
- configuração de auditoria estrita de autenticação;
- testes unitários do serviço de auditoria.

## Pendente e riscos

A gestão de sessão no runtime, claims de stamp/version, validação periódica do cookie, logout com revogação, telas, rate limit, bloqueio progressivo, testes HTTP/multi-tenant e job de CI não foram implementados nesta entrega. O SDK .NET ausente impediu comprovar build e testes; portanto a PR deve permanecer draft.

## Rollback operacional

Reverter o commit da aplicação restaura o comportamento anterior. A tabela nova é aditiva e pode permanecer sem uso. Se a remoção for indispensável, primeiro confirme que nenhum runtime passou a gravá-la e então remova `ged.authentication_session` em mudança operacional separada e aprovada. Não execute remoção automática em produção.
