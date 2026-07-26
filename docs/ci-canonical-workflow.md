# Workflow canônico de CI

`.github/workflows/inovaged-ci.yml` atende dispatch manual, pull requests e pushes em `main`, `develop`, `feature/**` e `codex/**`. Os jobs separam lint, solution/compilação, servidor Linux/PostgreSQL, agente Windows, guardas, migrations idempotentes, CMS E2E e contratos PoC.

Os workflows anteriores são preservados até o novo arquivo ser reconhecido e executado pelo GitHub. Depois disso, qualquer desativação deve ocorrer em PR separada e com histórico preservado.
