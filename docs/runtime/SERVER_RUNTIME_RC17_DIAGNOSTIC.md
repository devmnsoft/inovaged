# Server Runtime Stabilization RC17 — diagnóstico

## Escopo e conclusão do diagnóstico no repositório

Este relatório separa o que foi comprovado pelo build/publish do repositório do que ainda precisa ser coletado em `F:\Sistemas\ged`. O ambiente deste trabalho é Linux e **não possui acesso ao servidor de produção Windows**; portanto, nenhum resultado de IIS, serviço, processo ou arquivo em `F:` é presumido.

| Item | Resultado comprovado / ação no servidor |
|---|---|
| Target framework de Web e Infrastructure | `net8.0` |
| Npgsql | `8.0.6` direto; provider EF Core `8.0.11`. Npgsql 8 foi mantido deliberadamente para o runtime .NET 8; não houve downgrade. |
| DiagnosticSource | Referência direta `9.0.0` no executável Web, gerenciada centralmente. O artefato deve ser validado no publish limpo. |
| Runtime/SDK instalado no servidor | Pendente: executar os comandos abaixo no servidor. |
| `DiagnosticSource.dll` / `Npgsql.dll` em `F:\Sistemas\ged` | Pendente até a troca segura e validação no servidor. |
| Hospedagem (IIS, serviço ou `dotnet` direto) | Pendente de inspeção no servidor. |
| Caminho efetivo | O log informa `F:\Sistemas\ged`; confirmar processo e, se aplicável, Physical Path do IIS. |
| Banco efetivo | O log informa o banco administrativo `postgres`; migrar para `inovaged` somente após backup e provisionamento. |

## Causa e correção

A pilha recebida mostra que Npgsql tenta carregar `System.Diagnostics.DiagnosticSource, Version=9.0.0.0`, mas o artefato não está disponível na publicação em execução. Isso caracteriza publicação antiga/parcial ou conjunto de dependências incoerente, não uma migration ausente. A correção torna DiagnosticSource um ativo direto do projeto executável, exige publicação limpa e troca atômica da pasta. O Schema Health agora classifica separadamente `Healthy`, `SchemaOutdated`, `DatabaseUnavailable`, `RuntimeDependencyError` e `UnexpectedError`; falha de DLL não recomenda migration.

## Evidências obrigatórias a coletar em produção

Executar em PowerShell e anexar as saídas ao registro da mudança:

```powershell
dotnet --info
dotnet --list-runtimes
dotnet --list-sdks
Get-Item F:\Sistemas\ged\System.Diagnostics.DiagnosticSource.dll
Get-Item F:\Sistemas\ged\Npgsql.dll
Get-Item F:\Sistemas\ged\InovaGed.Web.dll
Get-Item F:\Sistemas\ged\InovaGed.Infrastructure.dll
Get-Process dotnet -ErrorAction SilentlyContinue | Select-Object Id,Path,StartTime
```

Para IIS, registrar site, AppPool, versão do Hosting Bundle e confirmar o Physical Path `F:\Sistemas\ged`. Para Windows Service, registrar nome, `PathName` e conta do serviço. Para execução direta, registrar linha de comando e diretório de trabalho.

## Critério de encerramento operacional

A mudança de código não comprova o estado do servidor. O incidente só deve ser encerrado após: implantação conforme o runbook RC17; ambos os DLLs presentes em `F:\Sistemas\ged`; banco `inovaged` confirmado; endpoints sem HTTP 500; e logs observados por múltiplos ciclos de todos os workers sem `FileNotFoundException`. A configuração do banco dedicado permanece **pendência operacional justificada** até acesso e backup de produção.
