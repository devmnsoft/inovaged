# Fluxo de login

O fluxo valida entrada, tenant/usuário, estado e senha; carrega roles canônicas; nega o acesso sem role; carrega setor; cria claims e cookie; somente então registra `LOGIN_SUCCESS`. Falha ao carregar autorização registra `LOGIN_ERROR_AUTHORIZATION_LOAD` com correlation ID e não cria cookie. Usuário sem role registra `LOGIN_DENIED_NO_ROLE`. O redirecionamento considera somente claims/roles, nunca username.
