# Diagnóstico inicial — IIS / DiagnosticSource (RC21)

Data da coleta: **2026-09-03 (UTC)**. Ambiente de diagnóstico: contêiner Linux do repositório.

## Comandos obrigatórios e resultado

| Comando | Resultado |
|---|---|
| `git status` | Branch `work`, árvore de trabalho limpa antes das alterações. |
| `dotnet --info` | Não executado: `dotnet: command not found`. O SDK .NET não está instalado neste contêiner. |
| `dotnet --list-runtimes` | Não executado pela mesma limitação do ambiente. |
| `dotnet list InovaGed.Web\InovaGed.Web.csproj package --include-transitive` | Não executado pela mesma limitação do ambiente. |
| `dotnet list InovaGed.Infrastructure\InovaGed.Infrastructure.csproj package --include-transitive` | Não executado pela mesma limitação do ambiente. |

Essa limitação não substitui a validação no host Windows: execute novamente os cinco comandos no servidor/agente que possui o SDK antes do deploy.

## Inspeção estática do repositório

1. O executável Web usa `TargetFramework` **net8.0**.
2. O repositório usa gerenciamento central (`ManagePackageVersionsCentrally=true`) em `Directory.Packages.props`.
3. A versão central direta de `Npgsql` é **8.0.6**; `Npgsql.EntityFrameworkCore.PostgreSQL` é **8.0.11**.
4. `System.Diagnostics.DiagnosticSource` está fixado em **9.0.0** e é referenciado diretamente pelo projeto Web, garantindo que o ativo de runtime seja incluído no publish.
5. Não foram encontradas versões de pacote declaradas diretamente nos `.csproj`; portanto não há conflito entre versão central e versão local. O grafo transitivo efetivo deve ser reconfirmado com `dotnet list ... --include-transitive` no host com SDK.

## Critério para o artefato

O publish só deve ser promovido se `scripts/windows/verify-iis-publish.ps1` confirmar a DLL Web, `web.config`, `Npgsql.dll`, `System.Diagnostics.DiagnosticSource.dll`, o `.deps.json` e a referência a DiagnosticSource dentro do `.deps.json`.
