# Relatório parcial — Evolução 04.1.25

Base inicial: `68e98f3c392b2b907f8fc605f223851ad9a2b8b0`.

Foram implementados contratos de modo/identidade, registro e heartbeat PostgreSQL, lease com fencing token, migration aditiva e separação live/ready. SingleNode segue como padrão. MultiNode e BlueGreen são configuráveis, mas **não homologados**: backplane, storage compartilhado, liderança em todos os schedulers, orquestração, ARR e suítes reais ainda são riscos abertos.

Nenhuma implantação, switch, merge ou rollback de banco foi executado. A reversão desta evolução consiste em restaurar a configuração SingleNode, remover o worker de heartbeat e reverter o commit; as tabelas devem ser preservadas para evidência ou removidas apenas por mudança manual aprovada.
