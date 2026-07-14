# Relatório de pacotes Identity e ASP.NET Core

## Escopo

Este relatório registra a revisão dos pacotes relacionados a Identity, autorização e criptografia usados pelo InovaGED em projetos `net8.0`, com foco na eliminação definitiva do downgrade `NU1605` envolvendo `Microsoft.AspNetCore.Cryptography.KeyDerivation`.

## Resultado da varredura

Com `bin` e `obj` excluídos, a busca por `Microsoft.AspNetCore.Cryptography.KeyDerivation`, `8.0.11` e `8.0.27` mostrou:

- `InovaGed.Infrastructure/InovaGed.Infrastructure.csproj` contém a única referência ativa direta a `Microsoft.AspNetCore.Cryptography.KeyDerivation`, em `8.0.27`.
- `InovaGed.Infrastructure/InovaGed.Infrastructure.csproj` contém `Microsoft.Extensions.Identity.Core` em `8.0.27`.
- Não há `Directory.Packages.props`, `Directory.Build.props`, `Directory.Build.targets`, `nuget.config` ou `packages.lock.json` ativos no repositório.
- Não há referência ativa a `Microsoft.AspNetCore.Cryptography.KeyDerivation` em `8.0.11` em `.csproj`, `.props`, `.targets` ou lock files.
- A string `8.0.11` remanescente em arquivo de projeto pertence a `Npgsql.EntityFrameworkCore.PostgreSQL`, não ao KeyDerivation.

## Pacotes revisados

| Pacote | Estado encontrado | Decisão | Justificativa |
| --- | --- | --- | --- |
| `Microsoft.AspNetCore.Cryptography.KeyDerivation` | Referência direta em `InovaGed.Infrastructure` com `8.0.27` | Manter | O projeto usa diretamente `KeyDerivation.Pbkdf2` em `Pbkdf2PasswordHasher`; a versão fica alinhada ao requisito transitivo de `Microsoft.Extensions.Identity.Core 8.0.27`. |
| `Microsoft.Extensions.Identity.Core` | Referência direta em `InovaGed.Infrastructure` com `8.0.27` | Manter | Necessário para os tipos de Identity usados na aplicação e compatível com `net8.0`. |
| `Microsoft.AspNet.Identity.Core` | Sem referência ativa em `.csproj` | Não reintroduzir | Pacote legado do ASP.NET Identity clássico; não há namespace `Microsoft.AspNet.Identity` ativo no código-fonte revisado. |
| `Microsoft.AspNetCore.Authorization` 2.x | Sem referência direta ativa em `.csproj` | Não reintroduzir | Em `net8.0`, os namespaces de autorização devem vir do shared framework `Microsoft.AspNetCore.App` quando necessário. |
| `Microsoft.AspNetCore.Mvc.Core` 2.x | Sem referência direta ativa em `.csproj` | Não reintroduzir | O projeto Web usa SDK Web/ASP.NET Core 8; pacotes MVC 2.x misturam stacks e aumentam risco de restore incoerente. |

## Consumidores mapeados

- `Pbkdf2PasswordHasher` consome `Microsoft.AspNetCore.Cryptography.KeyDerivation` diretamente.
- Controladores, handlers e serviços consomem `Microsoft.AspNetCore.Authorization` via ASP.NET Core 8/shared framework.
- Não foram localizados consumidores ativos de `Microsoft.AspNet.Identity` clássico.

## Conclusão

A correção segura é manter `KeyDerivation` e `Microsoft.Extensions.Identity.Core` alinhados em `8.0.27` no projeto Infrastructure, sem adicionar referências duplicadas nos consumidores e sem usar supressões como `NoWarn` ou redução de versão. A proteção contra regressão fica registrada no workflow `.github/workflows/dotnet-ci.yml`.
