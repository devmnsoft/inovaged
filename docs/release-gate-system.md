# Release gate sistêmico

O workflow `inovaged-ci` é o gate canônico. `release-gate` depende de `actionlint`, `solution-validation`, `server-linux`, `agent-windows`, `security-guards`, `migration-matrix`, `cms-e2e` e `poc-contract-tests` e aceita exclusivamente resultado `success`.

A proteção da `main` deve exigir PR, branch atualizada, uma aprovação, conversas resolvidas, ausência de force-push/delete e os nove checks `inovaged-ci / <job>`. Checks ausentes, cancelados ou skipped devem impedir merge. A configuração é externa ao repositório e precisa ser aplicada por administrador após o primeiro run reconhecer os nomes.

A PR permanece draft até todos os checks verdes. Não há deploy ou merge automático.
