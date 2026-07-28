# Eventos de auditoria do login

| Situação | Action | Outcome | EventType | ReasonCode |
|---|---|---|---|---|
| sucesso | LOGIN | SUCCESS | INFO | LOGIN_SUCCESS |
| erro ao carregar perfis | LOGIN | ERROR | ERROR | AUTHORIZATION_LOAD_ERROR |
| usuário sem perfil | LOGIN | DENIED | SECURITY | NO_ACCESS_ROLE |
| falha de credencial | LOGIN | FAILURE | SECURITY | código tipado da falha |

Senhas, hashes, cookies e tokens não integram os contextos nem comandos de auditoria.
