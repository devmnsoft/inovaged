# Máquina de estados do backup

Estados: `PENDING`, `CLAIMED`, `RUNNING`, `VERIFYING`, `COMPLETED`, `RETRY`, `FAILED`, `DEAD_LETTER`, `CANCELLED`. Terminais: `COMPLETED`, `FAILED`, `DEAD_LETTER`, `CANCELLED`. O catálogo `BackupJobStatuses` rejeita transições a partir de terminais. O claim seleciona `PENDING`/`RETRY`, respeita agenda e lease e usa `FOR UPDATE SKIP LOCKED` na mesma transação da mudança para `CLAIMED` e do evento.

O lease de 15 minutos só é estendido pelo worker proprietário nos estados ativos. Perda do lease registra `LEASE_LOST` e interrompe o fluxo.
