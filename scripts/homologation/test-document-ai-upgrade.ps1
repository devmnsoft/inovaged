[CmdletBinding()]
param([string]$Container = 'inovaged-ai-operational-pg')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = Join-Path $root 'artifacts/ai-operational'
$workspace = Join-Path $output ('upgrade-workspace-' + [guid]::NewGuid().ToString('N'))
$database = 'ai_upgrade_' + [guid]::NewGuid().ToString('N')
$manifest = Get-Content -Raw (Join-Path $root 'database/migrations.manifest.json') | ConvertFrom-Json
$originalConnection = $env:ConnectionStrings__DefaultConnection
$originalDirectory = Get-Location
New-Item -ItemType Directory -Force (Join-Path $workspace 'database/migrations'), (Join-Path $workspace 'database/base') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'InovaGed.sln') -Destination $workspace
Copy-Item -LiteralPath (Join-Path $root 'database/base/2026_05_schema.sql') -Destination (Join-Path $workspace 'database/base')
foreach ($migration in $manifest.migrations) { Copy-Item -LiteralPath (Join-Path $root $migration.path) -Destination (Join-Path $workspace $migration.path) }
$prefix = [Collections.Generic.List[object]]::new()
foreach ($migration in $manifest.migrations) {
    if ($migration.id -eq '2026_10_06_document_ai_review_identity_preflight') { break }
    $prefix.Add($migration)
}
$legacy = [pscustomobject]@{version=$manifest.version;migrations=$prefix.ToArray()}
$manifestPath = Join-Path $workspace 'database/migrations.manifest.json'
$legacy | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
$migrator = Join-Path $root 'InovaGed.Database.Migrator/bin/Debug/net8.0/InovaGed.Database.Migrator.dll'
try {
    docker exec $Container psql -X -q -v ON_ERROR_STOP=1 -U postgres -d postgres -c "create database $database;"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create disposable upgrade database.' }
    $env:ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=55439;Database=$database;Username=postgres;Password=local_disposable_only"
    Set-Location $workspace
    dotnet $migrator install 2>&1 | Tee-Object -FilePath (Join-Path $output 'upgrade-prefix.log')
    if ($LASTEXITCODE -ne 0) { throw 'Legacy prefix installation failed.' }
    @'
insert into ged.tenant(id,name,code) values('00000000-0000-0000-0000-000000000099','Upgrade Ficticio','upgrade-fixture');
insert into ged.ai_execution(id,tenant_id,user_id,task,provider,model,idempotency_key,input_fingerprint,policy_revision,state,expires_at,reservation_period)
values('99999999-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000099','99999999-0000-0000-0000-000000000002','ExtractMetadata','Deterministic','deterministic-v1','upgrade-fixture','fixture',1,'Completed',now()+interval '1 day',date_trunc('month',now())::date);
insert into ged.ai_suggestion_application(id,tenant_id,execution_id,document_id,version_id,task,reviewer_id,decision_fingerprint,decision_json,outcome,created_at)
select gen_random_uuid(),'00000000-0000-0000-0000-000000000099','99999999-0000-0000-0000-000000000001','99999999-0000-0000-0000-000000000003','99999999-0000-0000-0000-000000000004','ExtractMetadata','99999999-0000-0000-0000-000000000002',encode(sha256(convert_to(n::text,'UTF8')),'hex'),'{"title":"Revisao ficticia equivalente"}','Applied',now()+make_interval(secs=>n)
from generate_series(1,3) n;
create table ged.fixture_review_snapshot as select id,to_jsonb(a) as original from ged.ai_suggestion_application a;
create table ged.fixture_audit_link as select id as application_id from ged.ai_suggestion_application;
alter table ged.fixture_audit_link add foreign key(application_id) references ged.ai_suggestion_application(id);
'@ | docker exec -i $Container psql -X -q -v ON_ERROR_STOP=1 -U postgres -d $database
    if ($LASTEXITCODE -ne 0) { throw 'Legacy fixture failed.' }
    $legacy.migrations += @($manifest.migrations | Where-Object id -eq '2026_10_06_document_ai_review_recovery')
    $legacy | ConvertTo-Json -Depth 8 | Set-Content $manifestPath
    dotnet $migrator apply 2>&1 | Tee-Object -FilePath (Join-Path $output 'upgrade-reproduced-failure.log')
    if ($LASTEXITCODE -eq 0) { throw 'Published overflow was not reproduced.' }
    if (-not (Select-String -Path (Join-Path $output 'upgrade-reproduced-failure.log') -SimpleMatch 'SQLSTATE=22001' -Quiet)) { throw 'Failure was not the expected operation_key overflow.' }
    Copy-Item -LiteralPath (Join-Path $root 'database/migrations.manifest.json') -Destination $manifestPath -Force
    dotnet $migrator apply --retry-failed 2026_10_06_document_ai_review_recovery --verify 2>&1 | Tee-Object -FilePath (Join-Path $output 'upgrade-recovered.log')
    if ($LASTEXITCODE -ne 0) { throw 'Recovery through official migrator failed.' }
    $verified = @'
select (select count(*)=3 and count(distinct operation_key)=3 and min(length(operation_key))=64 and max(length(operation_key))=64 from ged.ai_suggestion_application)
and not exists(select 1 from ged.ai_suggestion_application a join ged.fixture_review_snapshot s using(id) where (to_jsonb(a)-'operation_key')<>s.original)
and (select count(*)=3 from ged.fixture_audit_link)
and (select count(*)=2 and count(distinct checksum_sha256)=1 from ged.schema_migration_history where script_name='2026_10_06_document_ai_review_recovery' and status in ('FAILED','APPLIED'));
'@ | docker exec -i $Container psql -X -q -A -t -v ON_ERROR_STOP=1 -U postgres -d $database
    if ($LASTEXITCODE -ne 0 -or ($verified -join '').Trim() -ne 't') { throw 'Upgrade preservation assertions failed.' }
    dotnet $migrator apply --verify 2>&1 | Tee-Object -FilePath (Join-Path $output 'upgrade-replay.log')
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade replay failed.' }
    'PASSOU: overflow reproduzido; retry oficial preserva revisoes, links e diario FAILED/APPLIED com o mesmo checksum.' | Tee-Object -FilePath (Join-Path $output 'upgrade-result.txt')
} finally {
    Set-Location $originalDirectory
    $env:ConnectionStrings__DefaultConnection = $originalConnection
}
