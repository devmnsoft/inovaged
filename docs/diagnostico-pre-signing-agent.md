# Diagnóstico pré Signing Agent CMS

- SHA inicial: `
a6a9fdd306b1173e6e226614a088c2cac0d96301
`
- Branch base local disponível: `work` (não há remoto/main configurado no clone atual).
- Data: 2026-07-21.
/bin/bash: line 7: dotnet: command not found
/bin/bash: line 7: dotnet: command not found
/bin/bash: line 7: dotnet: command not found
/bin/bash: line 7: dotnet: command not found
/bin/bash: line 7: dotnet: command not found
/bin/bash: line 7: dotnet: command not found


## Resultado do gate inicial

O ambiente não possui `dotnet` instalado (`dotnet: command not found`), portanto clean/restore/build/test/list não puderam ser executados localmente. `git diff --check` será executado após as alterações.
