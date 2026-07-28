# Eventos de auditoria de identidade

Este hotfix adiciona ao fluxo `LOGIN_DENIED_NO_ROLE` e `LOGIN_ERROR_AUTHORIZATION_LOAD`, preservando o correlation ID, e mantém `LOGIN_SUCCESS` somente após criação do cookie. Senha, hash e CPF não são gravados nesses eventos. A taxonomia administrativa completa solicitada depende da evolução da Central de Acessos e não é declarada como entregue.
