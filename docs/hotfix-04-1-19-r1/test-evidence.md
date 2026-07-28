# Evidências
Os checks estáticos locais (`git diff --check`, JSON e busca da guarda Razor) foram executados. Builds, publish, testes PostgreSQL e actionlint não foram executáveis porque o container não fornece `dotnet`, `psql` ou `actionlint`; permanecem gates obrigatórios do draft.
