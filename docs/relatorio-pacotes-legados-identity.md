# Relatório de pacotes legados de Identity e Authorization

## Escopo

Revisão do projeto `InovaGed.Infrastructure` e consumidores diretos de autenticação/autorização para evitar pacotes ASP.NET/ASP.NET Core 2.x em projetos `net8.0`.

## Evidências localizadas

| Pacote | Versão | Usos encontrados | Substituição | Risco | Decisão | Teste |
|---|---:|---|---|---|---|---|
| `Microsoft.AspNet.Identity.Core` | `2.2.4` | Nenhuma referência ativa em `.csproj`; nenhuma namespace `Microsoft.AspNet.Identity` localizado em código-fonte ativo. | `Microsoft.Extensions.Identity.Core 8.0.27` e tipos `Microsoft.AspNetCore.Identity`. | Alto se reaparecer, porque pertence ao Identity clássico pré-Core. | Não adicionar/remover no estado atual; manter bloqueado por revisão e CI existente. | `rg -n "Microsoft\.AspNet\.Identity|Microsoft\.AspNet\.Identity\.Core"`. |
| `Microsoft.AspNetCore.Authorization` | `2.3.0` | Nenhuma referência direta ativa em `.csproj`; namespaces `Microsoft.AspNetCore.Authorization` vêm do `FrameworkReference Include="Microsoft.AspNetCore.App"`. | `FrameworkReference Microsoft.AspNetCore.App` em projetos `net8.0`. | Médio se uma referência 2.x for reintroduzida, pois pode trazer APIs e assets antigos. | Manter via framework reference, sem pacote 2.x direto. | `rg -n "Microsoft\.AspNetCore\.Authorization"` e inspeção de `.csproj`. |
| `Microsoft.AspNetCore.Cryptography.KeyDerivation` | `8.0.27` | Uso direto em `InovaGed.Infrastructure/Security/Pbkdf2PasswordHasher.cs` via `KeyDerivation.Pbkdf2`. | Manter pacote direto em `8.0.27`, alinhado a `Microsoft.Extensions.Identity.Core 8.0.27`. | Baixo; patch alinhado elimina downgrade NU1605. | Referência direta preservada porque há uso direto. | `rg -n "KeyDerivation|Pbkdf2" InovaGed.Infrastructure`. |
| `Microsoft.Extensions.Identity.Core` | `8.0.27` | `PasswordHasher<ApplicationUser>` em seed e controladores, além da dependência transitiva para KeyDerivation. | Manter `8.0.27`. | Baixo; coerente com .NET 8. | Manter alinhado ao KeyDerivation. | Restore/build quando SDK .NET estiver disponível. |

## Observações

- Não foi feita remoção cega de autenticação.
- O projeto `InovaGed.Infrastructure` possui uso direto de `KeyDerivation.Pbkdf2`, portanto a referência direta a `Microsoft.AspNetCore.Cryptography.KeyDerivation` é justificada.
- Pacotes ASP.NET Core compartilhados devem continuar sendo consumidos preferencialmente via `FrameworkReference Include="Microsoft.AspNetCore.App"` quando aplicável a projetos `net8.0`.
