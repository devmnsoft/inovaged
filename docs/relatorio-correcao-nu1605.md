# Relatório de correção do NU1605

## Causa raiz

O downgrade era provocado por uma referência direta antiga de `Microsoft.AspNetCore.Cryptography.KeyDerivation` em patch antigo incompatível no ambiente local/árvore anterior, enquanto `Microsoft.Extensions.Identity.Core 8.0.27` exige transitivamente `Microsoft.AspNetCore.Cryptography.KeyDerivation >= 8.0.27`. A árvore atual já trazia `KeyDerivation 8.0.27`, mas ainda misturava pacotes ASP.NET Core 2.x (`Microsoft.AspNet.Identity.Core 2.2.4`, `Microsoft.AspNetCore.Authorization 2.3.0`, `Microsoft.AspNetCore 2.3.0`, `Microsoft.AspNetCore.Mvc.Core 2.3.0`) com projetos `net8.0`, aumentando o risco de restaurações incoerentes.

## Arquivos alterados

- `InovaGed.Infrastructure/InovaGed.Infrastructure.csproj`: manteve `KeyDerivation 8.0.27`, manteve `Microsoft.Extensions.Identity.Core 8.0.27`, removeu pacotes Identity/Authorization 2.x obsoletos e adicionou `FrameworkReference Microsoft.AspNetCore.App`.
- `InovaGed.Application/InovaGed.Application.csproj`: removeu pacotes ASP.NET Core 2.x e adicionou `FrameworkReference Microsoft.AspNetCore.App`.
- `InovaGed.Web/InovaGed.Web.csproj`: removeu `Microsoft.AspNetCore.Mvc.Core 2.3.0`, usando o shared framework do SDK Web.
- `.github/workflows/ci.yml`: adicionou listagem de dependências e validação contra `NU1605`, `KeyDerivation` em patch antigo incompatível e referências conflitantes.

## Versão resolvida esperada

- `Microsoft.AspNetCore.Cryptography.KeyDerivation`: `8.0.27`.
- `Microsoft.Extensions.Identity.Core`: `8.0.27`.
- ASP.NET Core Authorization/MVC base: shared framework `Microsoft.AspNetCore.App` do .NET 8.

## Comandos executados

- `git status --short`
- `git branch --show-current`
- `git remote -v`
- `git fetch --all --prune`
- `git pull --rebase` (falhou porque não há upstream/remotes configurados)
- `git diff main -- InovaGed.Infrastructure/InovaGed.Infrastructure.csproj` (falhou porque não há referência local `main`)
- `rg -n "Microsoft\.AspNetCore\.Cryptography\.KeyDerivation|8\.0\.11|8\.0\.27" --glob '!**/bin/**' --glob '!**/obj/**'`
- `find . \( -name packages.lock.json -o -name project.assets.json \) -print`
- `dotnet nuget locals all --clear` (não executável neste ambiente: `dotnet` ausente)
- `dotnet restore InovaGed.Infrastructure/InovaGed.Infrastructure.csproj --force --no-cache` (não executável neste ambiente: `dotnet` ausente)
- `dotnet restore InovaGed.sln --force --no-cache` (não executável neste ambiente: `dotnet` ausente)

## Resultado do restore, build e testes

Não foi possível executar restore/build/test localmente porque o contêiner não possui o SDK .NET instalado (`dotnet: command not found`). A validação foi codificada no GitHub Actions para executar em ambiente com .NET 8.
