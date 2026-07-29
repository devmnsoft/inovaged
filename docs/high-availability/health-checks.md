# Health checks

* `/health/live`: somente o check `self`, sem PostgreSQL, Redis ou storage.
* `/health/ready`: catálogo atual de dependências; deve ser usado pelo balanceador.
* `/health/node`: envelope público mínimo sem endpoints, paths ou segredos.

Uma evolução antes da homologação deve ligar readiness ao heartbeat, estado de drain, key ring, backplane Redis e storage compartilhado.
