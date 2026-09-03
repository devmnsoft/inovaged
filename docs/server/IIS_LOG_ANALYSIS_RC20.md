# Análise dos logs IIS — RC20

## Eventos observados
Os acessos a `/.env`, `/.git/config`, `/appsettings.json`, `/web.config`, `/phpunit`, `/xmlrpc.php`, `/wp-includes`, `/terraform.tfvars`, `/docker-compose.yml`, `/mcp`, `/api/mcp` e `/boaform/admin/formLogin` têm o padrão de varredura automatizada externa. Os `POST /` com 405 anexados também vieram de endereços externos.

## Separação técnica
A amostra recebida não contém identidade autenticada que associe `POST /` a um usuário e não comprova falha de botão. Também não traz evidência suficiente de POST autenticado para `/Labels/Preview`, `/Labels/PrintPreview`, `/Labels/Print` ou `/Labels/PrintLocDesk`. Essas ações passam a ser correlacionadas por `LABEL_ACTION_SUBMITTED` e `ClientActionId`, sem conteúdo base64.

## Conclusão
Scanner externo é bloqueado/classificado por IIS e middleware, fora da fila de incidentes de negócio. A validação dos botões deve usar navegador autenticado, Network e o evento correlacionado.
