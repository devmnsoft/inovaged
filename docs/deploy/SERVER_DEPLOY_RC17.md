# Server Runtime Stabilization RC17 — runbook de implantação

## 1. Pré-verificação e banco dedicado

1. Identifique IIS, Windows Service ou execução direta e confirme que aponta para `F:\Sistemas\ged`.
2. Faça backup verificável do banco atual antes de qualquer mudança.
3. Em uma sessão administrativa PostgreSQL, crie proprietário e banco (substitua a senha por segredo do cofre; não grave senha real neste arquivo):

```sql
CREATE ROLE inovaged_app LOGIN PASSWORD 'SUBSTITUIR_PELO_SEGREDO';
CREATE DATABASE inovaged OWNER inovaged_app;
REVOKE ALL ON DATABASE inovaged FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE inovaged TO inovaged_app;
```

Depois de conectar ao banco `inovaged`, conceda somente `USAGE`/privilégios de objetos exigidos pela aplicação. O proprietário pode ser usado para a primeira instalação; após migrations, considere uma role separada para migrations e reduza o usuário de runtime. Nunca execute as migrations no banco `postgres` quando o destino é `inovaged`.

Configure pelo mecanismo adotado pelo host, por exemplo:

```powershell
setx ConnectionStrings__DefaultConnection "Host=127.0.0.1;Port=5432;Database=inovaged;Username=inovaged_app;Password=SENHA;Pooling=true;Maximum Pool Size=100;Timeout=30;Command Timeout=120;Application Name=InovaGED" /M
```

No IIS, prefira variável protegida no AppPool/configuração de implantação. Reinicie o processo para ler a variável. Só então aplique `database/apply_all_required_migrations.sql` **no banco inovaged** e registre banco, usuário, horário e resultado.

## 2. Gerar uma publicação limpa

Em uma árvore limpa do commit aprovado:

```powershell
if (Test-Path F:\Sistemas\ged_publish_temp) { Remove-Item F:\Sistemas\ged_publish_temp -Recurse -Force }
dotnet clean InovaGed.sln
dotnet nuget locals all --clear
dotnet restore InovaGed.sln
dotnet build InovaGed.sln -c Release -v:minimal
dotnet publish InovaGed.Web\InovaGed.Web.csproj -c Release -o F:\Sistemas\ged_publish_temp
Test-Path F:\Sistemas\ged_publish_temp\System.Diagnostics.DiagnosticSource.dll
Test-Path F:\Sistemas\ged_publish_temp\Npgsql.dll
```

Os dois últimos comandos devem retornar `True`. Não continue em caso contrário e não copie DLL isolada. Confira também `InovaGed.Web.dll`, `InovaGed.Infrastructure.dll`, `.deps.json`, `.runtimeconfig.json` e `web.config` como um único conjunto.

Se o runtime/Hosting Bundle do servidor estiver ausente, incompleto ou divergente, corrija o Hosting Bundle. Como alternativa documentada, refaça **todo** o artefato:

```powershell
dotnet publish InovaGed.Web\InovaGed.Web.csproj -c Release -r win-x64 --self-contained true -o F:\Sistemas\ged_publish_temp
```

## 3. Parar, preservar e trocar atomicamente

Pare o AppPool/site específico (preferível a indisponibilizar todo o IIS), o serviço correto ou o processo supervisionado. Faça inventário e cópia dos dados persistentes: `appsettings.Production.json` externo, `uploads`, `storage`, `logs`, certificados, logos, anexos e quaisquer diretórios configurados fora do artefato.

```powershell
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
Rename-Item F:\Sistemas\ged "F:\Sistemas\ged_backup_$stamp"
Rename-Item F:\Sistemas\ged_publish_temp F:\Sistemas\ged
```

Restaure os itens persistentes do backup sem sobrescrever binários publicados e confira ACLs/identidade do AppPool ou serviço. Inicie novamente. Nunca apague o backup ou dados de usuário durante a janela.

## 4. Validar aplicação, Schema Health e workers

Confirme os DLLs em `F:\Sistemas\ged`, o caminho efetivo do processo e a conexão apontando para `Database=inovaged`. Acesse `/status`, `/Home/Status`, `/SchemaHealth`, `/DatabaseReadiness`, `/Administration`, `/Labels/History` e `/Labels/PrintWizard`; nenhum deve retornar 500.

Observe logs por pelo menos três ciclos de `SchemaHealthService`, `GedProcessingWorker`, `OcrWorker`, `LoanOverdueWorker` e `RetentionDailyWorker`. Não deve existir `Could not load file or assembly System.Diagnostics.DiagnosticSource`. Registre separadamente falha de conexão, schema desatualizado, dependência runtime e erro inesperado. Somente `SchemaOutdated` com tabelas/colunas realmente ausentes justifica sugerir migrations.

## 5. Rollback

Se a validação falhar, pare novamente o host, renomeie `F:\Sistemas\ged` para uma pasta de artefato rejeitado e devolva `F:\Sistemas\ged_backup_<stamp>` para `F:\Sistemas\ged`. Reverta também a connection string apenas se o plano de mudança assim determinar; não reverta banco sem procedimento próprio e backup. Reinicie, valide saúde e guarde logs e artefato rejeitado para análise.
