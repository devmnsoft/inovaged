# Relatório da correção definitiva do NU1605 - KeyDerivation

## Causa raiz real

A cadeia de dependência exige `Microsoft.AspNetCore.Cryptography.KeyDerivation >= 8.0.27` porque `Microsoft.Extensions.Identity.Core` está em `8.0.27`. A origem ativa da correção fica em `InovaGed.Infrastructure/InovaGed.Infrastructure.csproj`, que contém a referência direta ao pacote `Microsoft.AspNetCore.Cryptography.KeyDerivation` e também referencia `Microsoft.Extensions.Identity.Core`.

## Arquivo responsável

- `InovaGed.Infrastructure/InovaGed.Infrastructure.csproj`.

## Versão anterior e versão final

- Versão incompatível investigada: `8.0.11`.
- Versão final ativa: `8.0.27`.
- `Microsoft.Extensions.Identity.Core`: `8.0.27`.

## Resultado da varredura

Comando executado:

```bash
rg -n "Microsoft\.AspNetCore\.Cryptography\.KeyDerivation|8\.0\.11|8\.0\.27" --glob '!**/bin/**' --glob '!**/obj/**'
```

Resultado funcional:

- `KeyDerivation` ativo apenas em `InovaGed.Infrastructure/InovaGed.Infrastructure.csproj` com versão `8.0.27` e no código `Pbkdf2PasswordHasher`.
- Não há `Directory.Packages.props`, `Directory.Build.props`, `Directory.Build.targets`, `nuget.config`, `global.json` ou `packages.lock.json` ativos no repositório.
- A string `8.0.11` remanescente está relacionada a outro pacote (`Npgsql.EntityFrameworkCore.PostgreSQL`) e a documentação histórica, não a `Microsoft.AspNetCore.Cryptography.KeyDerivation` em arquivos de pacote ativos.

## Caches removidos

- `bin/` e `obj/` foram removidos por varredura local quando presentes.
- `dotnet nuget locals all --clear` não pôde ser executado neste ambiente porque o SDK `dotnet` não está instalado no container.

## Lock files

- Nenhum `packages.lock.json` versionado foi localizado fora de `bin/`/`obj/`.
- Não há `RestoreLockedMode` ativo localizado em arquivos de projeto/props/targets.

## Árvore de dependência esperada

- `InovaGed.Infrastructure` -> `Microsoft.AspNetCore.Cryptography.KeyDerivation 8.0.27`.
- `InovaGed.Infrastructure` -> `Microsoft.Extensions.Identity.Core 8.0.27` -> `Microsoft.AspNetCore.Cryptography.KeyDerivation >= 8.0.27`.
- `InovaGed.Web`, `WebGed.WebApi` e `InovaGed.Application.Tests` recebem a versão coerente por referência de projeto/transitividade sem receberem referências redundantes para mascarar o erro.

## Restore, build e testes

O ambiente de execução atual não possui o SDK .NET (`dotnet: command not found`). Por isso, os comandos obrigatórios ficam documentados para execução no CI e em ambiente de desenvolvimento com SDK .NET 8:

```bash
dotnet restore InovaGed.sln --force --no-cache
dotnet build InovaGed.sln --no-restore -c Debug
dotnet test InovaGed.sln --no-build -c Debug
```

## CI

Foi criado o workflow `.github/workflows/dotnet-ci.yml` com:

- guarda contra `Microsoft.AspNetCore.Cryptography.KeyDerivation` em `8.0.11` nos arquivos `.csproj`, `.props` e `.targets`;
- `dotnet restore InovaGed.sln`;
- `dotnet build InovaGed.sln --no-restore --configuration Release`;
- `dotnet test InovaGed.sln --no-build --configuration Release`.

## Riscos restantes

- O container local não permite validar restore/build/test por ausência do SDK .NET.
- Há documentação histórica mencionando `8.0.11`; isso não é referência ativa de pacote, mas pode confundir futuras revisões.
- Deve-se manter atenção a pacotes Microsoft 2.x caso sejam reintroduzidos em `.csproj`.
