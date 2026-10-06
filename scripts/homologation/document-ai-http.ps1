# Verificacao HTTP do ciclo documental assistido.
# Nao trata redirecionamento para login como autenticacao nem como autorizacao.
# Provedor Deterministic exige INOVAGED_AI_DETERMINISTIC=1 na aplicacao local.
# Nao homologa Groq, Gemini ou DeepSeek.
# Exit 0: verificacoes obrigatorias solicitadas passaram. Exit 1: falha. Exit 2: obrigatoria nao executada.
[CmdletBinding()]
param([switch]$RequireRealProviders, [switch]$RequireVisual)

$ErrorActionPreference = 'Stop'
$base = $env:INOVAGED_AI_HTTP_BASE
if ([string]::IsNullOrWhiteSpace($base)) {
    Write-Output 'NAO EXECUTADO: defina INOVAGED_AI_HTTP_BASE com a URL local. Nao use producao.'
    exit 2
}
$baseUri = [Uri]$base
if (-not $baseUri.IsAbsoluteUri -or -not $baseUri.IsLoopback) { throw 'A matriz local exige URL de loopback.' }
$required = @(
    'INOVAGED_AI_HTTP_EDITOR_EMAIL', 'INOVAGED_AI_HTTP_EDITOR_PASSWORD',
    'INOVAGED_AI_HTTP_READER_EMAIL', 'INOVAGED_AI_HTTP_READER_PASSWORD',
    'INOVAGED_AI_HTTP_SECRECY_EMAIL', 'INOVAGED_AI_HTTP_SECRECY_PASSWORD',
    'INOVAGED_AI_HTTP_DENIED_EMAIL', 'INOVAGED_AI_HTTP_DENIED_PASSWORD',
    'INOVAGED_AI_HTTP_OTHER_TENANT_EMAIL', 'INOVAGED_AI_HTTP_OTHER_TENANT_PASSWORD',
    'INOVAGED_AI_HTTP_DOCUMENT_ID', 'INOVAGED_AI_HTTP_VERSION_ID', 'INOVAGED_AI_HTTP_EDITOR_ID'
)
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    Write-Output ('NAO EXECUTADO: faltam variaveis ficticias: ' + ($missing -join ', '))
    exit 2
}

$results = New-Object System.Collections.Generic.List[string]
$failed = $false
function Add-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    $state = $(if ($Passed) { 'PASSOU' } else { 'FALHOU' })
    if (-not $Passed) { $script:failed = $true }
    $script:results.Add("$state | $Name | $Detail")
    Write-Output "$state | $Name | $Detail"
}
function Read-Body($response) {
    if ($null -eq $response) { return '' }
    if ($response -is [System.Net.Http.HttpResponseMessage]) { return $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
    $stream = $response.GetResponseStream()
    if ($null -eq $stream) { return '' }
    $reader = New-Object System.IO.StreamReader($stream)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Invoke-App([Microsoft.PowerShell.Commands.WebRequestSession]$Session, [string]$Method, [string]$Url, $Body, [bool]$AllowLoginRedirect = $false) {
    try {
        $request = @{ Uri = $Url; WebSession = $Session; Method = $Method; UseBasicParsing = $true; MaximumRedirection = 0 }
        if ($null -ne $Body) { $request.Body = $Body }
        $response = Invoke-WebRequest @request
        return @{ Code = [int]$response.StatusCode; Body = $response.Content; Location = [string]$response.Headers['Location'] }
    } catch {
        $http = $_.Exception.Response
        if ($null -eq $http) { throw }
        $code = [int]$http.StatusCode
        $location = ''
        try {
            $location = if ($http -is [System.Net.Http.HttpResponseMessage]) { [string]$http.Headers.Location } else { [string]$http.Headers['Location'] }
        } catch { }
        $text = ''
        try { $text = Read-Body $http } catch { }
        if (-not $AllowLoginRedirect -and $location -match '/Account/Login') {
            return @{ Code = $code; Body = $text; Location = $location; LoginRedirect = $true }
        }
        return @{ Code = $code; Body = $text; Location = $location; LoginRedirect = $false }
    }
}
function Get-Token([Microsoft.PowerShell.Commands.WebRequestSession]$Session, [string]$Url) {
    $page = Invoke-App $Session 'GET' $Url $null $true
    if ($page.LoginRedirect -or $page.Location -match '/Account/Login') { return $null }
    $match = [regex]::Match($page.Body, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
    if (-not $match.Success) { $match = [regex]::Match($page.Body, 'value="([^"]+)"[^>]*name="__RequestVerificationToken"') }
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}
function Login-Profile([string]$Email, [string]$Password, [string]$TenantSlug = 'default') {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPage = Invoke-App $session 'GET' "$base/Account/Login" $null $true
    $token = ([regex]::Match($loginPage.Body, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($token)) { return @{ Authenticated = $false; Session = $session; Detail = 'token de login ausente' } }
    $posted = Invoke-App $session 'POST' "$base/Account/Login" @{ Email = $Email; Password = $Password; TenantSlug = $TenantSlug; __RequestVerificationToken = $token } $true
    $landedOnLogin = $posted.Location -match '/Account/Login' -or ($posted.Code -eq 200 -and $posted.Body -match 'id="loginForm"')
    $landingPath = if ($posted.Location -and $posted.Location.StartsWith('/')) { $posted.Location } else { '/' }
    $landing = Invoke-App $session 'GET' ($base + $landingPath) $null $true
    $authenticated = $posted.Code -eq 302 -and $landing.Code -eq 200 -and -not $landedOnLogin -and -not $landing.Location -and $landing.Body -notmatch 'id="loginForm"'
    return @{ Authenticated = $authenticated; Session = $session; Detail = "login=$($posted.Code) destino=$($posted.Location) inicio=$($landing.Code) redirectLogin=$($landing.LoginRedirect)" }
}
function Convert-JsonSafe([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $trim = $Text.Trim()
    if (-not $trim.StartsWith('{') -and -not $trim.StartsWith('[')) { return $null }
    return $trim | ConvertFrom-Json
}
function Invoke-FixtureSql([string]$Sql) {
    $fixtureDatabase = $env:INOVAGED_AI_HTTP_FIXTURE_DATABASE
    if ($fixtureDatabase -notmatch '^ai_install_[a-f0-9]{32}$' -or $env:INOVAGED_AI_HTTP_DOCUMENT_ID -ne 'dddddddd-dddd-dddd-dddd-dddddddddd01') {
        throw 'Mutacoes de teste exigem o banco e documento ficticios do roteiro local.'
    }
    $fixtureOutput = $Sql | docker exec -i inovaged-ai-operational-pg psql -X -q -A -t -v ON_ERROR_STOP=1 -U postgres -d $fixtureDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Falha no SQL da fixture descartavel.' }
    return ($fixtureOutput -join "`n").Trim()
}
function New-MetadataSuggestion($Session, [string]$AntiForgery) {
    $response = Invoke-App $Session 'POST' "$base/HospitalDocuments/SuggestMetadata" @{ versionId=$versionId; idempotencyKey=[guid]::NewGuid().ToString(); __RequestVerificationToken=$AntiForgery }
    $data = Convert-JsonSafe $response.Body
    if ($response.Code -ne 200 -or -not $data.executionId) { throw "Geracao obrigatoria falhou: HTTP $($response.Code)" }
    return $data
}
function Apply-FixtureMetadata($Session, [string]$AntiForgery, $Suggestion, [hashtable]$Overrides = @{}) {
    $body = @{ documentId=$documentId; versionId=$versionId; executionId=[string]$Suggestion.executionId; concurrencyToken=[string]$Suggestion.concurrencyToken; titleSet='true'; title='Revisao negativa nao deve gravar'; descriptionSet='false'; isConfidentialSet='false'; __RequestVerificationToken=$AntiForgery }
    foreach ($key in $Overrides.Keys) { $body[$key]=$Overrides[$key] }
    return Invoke-App $Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" $body
}
function Assert-NoApplication([string]$Name, $ExecutionId) {
    $safeId = ([guid]$ExecutionId).ToString()
    $count = Invoke-FixtureSql "select count(*) from ged.ai_suggestion_application where execution_id='$safeId';"
    Add-Result ($Name + ' sem revisao persistida') ($count -eq '0') "revisoes=$count"
}
function Get-HistoryCount([Microsoft.PowerShell.Commands.WebRequestSession]$Session, [string]$DocumentId) {
    $response = Invoke-App $Session 'GET' "$base/HospitalDocuments/ReviewHistory?documentId=$DocumentId&page=1&pageSize=20" $null
    $json = Convert-JsonSafe $response.Body
    $total = 0
    if ($null -ne $json -and $null -ne $json.total) { $total = [int]$json.total }
    return @{ Code = $response.Code; Json = $json; Total = $total; LoginRedirect = $response.LoginRedirect }
}

$incomplete = $false
function Add-Skipped([string]$Name, [string]$Detail) {
    $script:incomplete = $true
    $script:results.Add("NAO EXECUTADO | $Name | $Detail")
    Write-Output "NAO EXECUTADO | $Name | $Detail"
}
$documentId = $env:INOVAGED_AI_HTTP_DOCUMENT_ID
$versionId = $env:INOVAGED_AI_HTTP_VERSION_ID
$editor = Login-Profile $env:INOVAGED_AI_HTTP_EDITOR_EMAIL $env:INOVAGED_AI_HTTP_EDITOR_PASSWORD
Add-Result 'autenticacao do editor' $editor.Authenticated $editor.Detail
if (-not $editor.Authenticated) { Write-Output 'FALHA: a sessao do editor nao foi comprovada.'; exit 1 }

$viewerUrl = "$base/HospitalDocuments/Viewer?documentId=$documentId&versionId=$versionId"
$viewer = Invoke-App $editor.Session 'GET' $viewerUrl $null
$token = Get-Token $editor.Session $viewerUrl
$historyOnPage = $viewer.Code -eq 200 -and -not $viewer.LoginRedirect -and $viewer.Body.Contains('data-ai-review-history') -and $viewer.Body.Contains('Histórico de revisões de IA')
Add-Result 'visualizador autenticado expoe o historico' ($null -ne $token -and $historyOnPage) ("HTTP $($viewer.Code) token=$($null -ne $token)")

$noToken = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/SuggestMetadata" @{ versionId = $versionId; idempotencyKey = [guid]::NewGuid().ToString() }
Add-Result 'sem antiforgery' ($noToken.Code -ge 400 -and $noToken.Code -ne 302) ("HTTP " + $noToken.Code)

$before = Get-HistoryCount $editor.Session $documentId
Add-Result 'historico inicial autorizado' ($before.Code -eq 200 -and $null -ne $before.Json -and $before.Json.success -eq $true) ("HTTP $($before.Code) total=$($before.Total)")

$suggested = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/SuggestMetadata" @{ versionId = $versionId; idempotencyKey = [guid]::NewGuid().ToString(); __RequestVerificationToken = $token }
$suggestedJson = Convert-JsonSafe $suggested.Body
$suggestOk = $suggested.Code -eq 200 -and $null -ne $suggestedJson -and $suggestedJson.success -eq $true -and $suggestedJson.executionId
Add-Result 'geracao de sugestao de metadados' $suggestOk ("HTTP $($suggested.Code) $($suggested.Body)")
if ($suggestOk) {
    $executionId = [string]$suggestedJson.executionId
    $concurrency = [string]$suggestedJson.concurrencyToken
    $title = 'Titulo homologado ' + ([guid]::NewGuid().ToString('N').Substring(0, 8))
    $applied = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" @{ documentId = $documentId; versionId = $versionId; executionId = $executionId; concurrencyToken = $concurrency; titleSet = 'true'; title = $title; descriptionSet = 'false'; isConfidentialSet = 'false'; __RequestVerificationToken = $token }
    $appliedJson = Convert-JsonSafe $applied.Body
    $applyOk = $applied.Code -eq 200 -and $null -ne $appliedJson -and $appliedJson.success -eq $true -and $appliedJson.alreadyApplied -ne $true
    Add-Result 'aplicacao da revisao' $applyOk ("HTTP $($applied.Code) $($applied.Body)")
    $after = Get-HistoryCount $editor.Session $documentId
    $reviews = @($after.Json.items | Where-Object {
        $_.kind -eq 'application' -and $_.executionId -eq $executionId -and $_.task -eq 'ExtractMetadata' -and
        $_.documentId -eq $documentId -and $_.versionId -eq $versionId -and $_.reviewerId -eq $env:INOVAGED_AI_HTTP_EDITOR_ID -and
        @($_.fields | Where-Object { $_.name -eq 'title' -and $_.value -eq $title }).Count -eq 1
    })
    Add-Result 'historico registra identidade e decisao' ($after.Code -eq 200 -and $reviews.Count -eq 1 -and $after.Total -eq ($before.Total + 1)) ("execucao=$executionId revisoes=$($reviews.Count)")
    $replay = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" @{ documentId = $documentId; versionId = $versionId; executionId = $executionId; concurrencyToken = '1'; titleSet = 'true'; title = $title; descriptionSet = 'false'; isConfidentialSet = 'false'; __RequestVerificationToken = $token }
    $replayJson = Convert-JsonSafe $replay.Body
    $replayOk = $replay.Code -eq 200 -and $null -ne $replayJson -and $replayJson.alreadyApplied -eq $true -and ($replayJson.retentionState -eq 'nao-aplicavel' -and $replayJson.retentionRecalculated -eq $false)
    Add-Result 'repeticao idempotente' $replayOk ("HTTP $($replay.Code) $($replay.Body)")
    $afterReplay = Get-HistoryCount $editor.Session $documentId
    Add-Result 'repeticao nao aumenta o historico' ($afterReplay.Total -eq $after.Total) ("total=$($afterReplay.Total)")
    $conflict = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" @{ documentId = $documentId; versionId = $versionId; executionId = $executionId; concurrencyToken = $concurrency; titleSet = 'true'; title = ($title + ' outro'); descriptionSet = 'false'; isConfidentialSet = 'false'; __RequestVerificationToken = $token }
    $conflictJson = Convert-JsonSafe $conflict.Body
    Add-Result 'mesma revisao com outra decisao' ($conflict.Code -eq 409) ("HTTP $($conflict.Code) $($conflict.Body)")
    $afterConflict = Get-HistoryCount $editor.Session $documentId
    Add-Result 'conflito nao grava outra revisao' ($afterConflict.Total -eq $after.Total) ("total=$($afterConflict.Total)")
    if ($env:INOVAGED_AI_HTTP_CLASS_ID) {
        if ($env:INOVAGED_AI_HTTP_FIXTURE_DATABASE) {
            Invoke-FixtureSql "update ged.document set classification_id=null, classification_version_id=null, retention_hold=true, retention_hold_reason='HOLD ficticio de homologacao' where id='$documentId';" | Out-Null
        }
        $archival = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/SuggestArchivalClassification" @{ versionId = $versionId; idempotencyKey = [guid]::NewGuid().ToString(); __RequestVerificationToken = $token }
        $archivalJson = Convert-JsonSafe $archival.Body
        $archivalOk = $archival.Code -eq 200 -and $null -ne $archivalJson -and $archivalJson.executionId
        Add-Result 'sugestao de classificacao arquivistica' $archivalOk ("HTTP $($archival.Code)")
        if ($archivalOk) {
            if ($env:INOVAGED_AI_HTTP_FIXTURE_DATABASE) {
                Invoke-FixtureSql @'
create or replace function ged.fixture_retention_failure() returns trigger language plpgsql as $$
begin
 if new.document_id='dddddddd-dddd-dddd-dddd-dddddddddd01' and new.resolved_at is not null then raise exception 'fixture_recovery_failure'; end if;
 return new;
end $$;
create trigger fixture_retention_failure before update on ged.ai_retention_recalc_pending for each row execute function ged.fixture_retention_failure();
'@ | Out-Null
            }
            try {
            $classApply = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyArchivalClassification" @{ documentId = $documentId; versionId = $versionId; executionId = [string]$archivalJson.executionId; concurrencyToken = [string]$archivalJson.concurrencyToken; classificationId = $env:INOVAGED_AI_HTTP_CLASS_ID; __RequestVerificationToken = $token }
            } finally {
                if ($env:INOVAGED_AI_HTTP_FIXTURE_DATABASE) { Invoke-FixtureSql 'drop trigger if exists fixture_retention_failure on ged.ai_retention_recalc_pending; drop function if exists ged.fixture_retention_failure();' | Out-Null }
            }
            $classJson = Convert-JsonSafe $classApply.Body
            $classOk = $classApply.Code -eq 200 -and $null -ne $classJson -and $classJson.success -eq $true -and ($classJson.retentionPending -eq $true -or $classJson.retentionRecalculated -eq $true)
            Add-Result 'aplicacao com temporalidade confirmada ou pendente' $classOk ("HTTP $($classApply.Code) $($classApply.Body)")
            if ($classJson.retentionPending -eq $true) {
                $pending = Invoke-App $editor.Session 'GET' "$base/HospitalDocuments/RetentionPending?documentId=$documentId" $null
                $pendingJson = Convert-JsonSafe $pending.Body
                $pendingId = $null
                if ($pendingJson.items) { $pendingId = [string]$pendingJson.items[0].id }
                Add-Result 'consulta de pendencia' ($pending.Code -eq 200 -and $pendingId) ("HTTP $($pending.Code)")
                if ($pendingId) {
                    if ($env:INOVAGED_AI_HTTP_FIXTURE_DATABASE) {
                        $safePending = ([guid]$pendingId).ToString()
                        Invoke-FixtureSql "update ged.ai_retention_recalc_pending set next_attempt_at=now() where id='$safePending';" | Out-Null
                        $deadline = [DateTime]::UtcNow.AddSeconds(65)
                        do {
                            $resolved = Invoke-FixtureSql "select resolved_at is not null from ged.ai_retention_recalc_pending where id='$safePending';"
                            if ($resolved -eq 't') { break }
                            Start-Sleep -Seconds 2
                        } while ([DateTime]::UtcNow -lt $deadline)
                        Add-Result 'worker recupera pendencia automaticamente' ($resolved -eq 't') "resolvida=$resolved"
                        $hold = Invoke-FixtureSql "select retention_hold and retention_hold_reason='HOLD ficticio de homologacao' and disposed_at is null from ged.document where id='$documentId';"
                        Add-Result 'recuperacao preserva HOLD' ($hold -eq 't') "hold=$hold"
                    }
                    $retry = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/RetryRetention" @{ documentId = $documentId; pendingId = $pendingId; __RequestVerificationToken = $token }
                    $retryJson = Convert-JsonSafe $retry.Body
                    $retryOk = $retry.Code -eq 200 -and $null -ne $retryJson -and ($retryJson.retentionState -eq 'resolvida' -or $retryJson.retentionState -eq 'recuperada' -or $retryJson.retentionState -eq 'concluida' -or $retryJson.retentionState -eq 'pendente' -or $retryJson.retentionState -eq 'em processamento')
                    Add-Result 'nova tentativa de temporalidade' $retryOk ("HTTP $($retry.Code) $($retry.Body)")
                }
            }
        }
    } else { Add-Skipped 'classificacao e recuperacao de temporalidade' 'defina INOVAGED_AI_HTTP_CLASS_ID ficticio' }
} else { Add-Skipped 'revisao, aplicacao, repeticao e conflito' 'a geracao nao devolveu execucao. Confira OCR ficticio, provedor Deterministic e politica do tenant.' }

$reader = Login-Profile $env:INOVAGED_AI_HTTP_READER_EMAIL $env:INOVAGED_AI_HTTP_READER_PASSWORD
Add-Result 'leitor autentica' $reader.Authenticated $reader.Detail
if ($reader.Authenticated) {
    $readerHistory = Get-HistoryCount $reader.Session $documentId
    Add-Result 'leitor consulta o historico' ($readerHistory.Code -eq 200 -and $readerHistory.Json.success -eq $true) ("HTTP $($readerHistory.Code)")
    $readerToken = Get-Token $reader.Session $viewerUrl
    $readerApply = Invoke-App $reader.Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" @{ documentId = $documentId; versionId = $versionId; executionId = [guid]::NewGuid().ToString(); concurrencyToken = '1'; titleSet = 'true'; title = 'nao gravar'; __RequestVerificationToken = $readerToken }
    Add-Result 'leitor nao aplica' ($readerApply.Code -in 403, 404) ("HTTP $($readerApply.Code) destino=$($readerApply.Location) sessao=$($reader.Authenticated)")
} else { Add-Skipped 'leitor consulta e nao aplica' $reader.Detail }

$denied = Login-Profile $env:INOVAGED_AI_HTTP_DENIED_EMAIL $env:INOVAGED_AI_HTTP_DENIED_PASSWORD
Add-Result 'usuario sem acesso autentica' $denied.Authenticated $denied.Detail
if ($denied.Authenticated) {
    foreach ($route in @('Viewer','Preview','OcrText')) {
        $response = Invoke-App $denied.Session 'GET' "$base/HospitalDocuments/${route}?versionId=$versionId" $null
        Add-Result ("documento sem ACL bloqueia " + $route) ($response.Code -in 403,404) "HTTP $($response.Code)"
    }
    foreach ($route in @('Search?q=homolog','Suggestions?q=homolog','Summary')) {
        $warm = Invoke-App $editor.Session 'GET' "$base/HospitalDocuments/$route" $null
        $response = Invoke-App $denied.Session 'GET' "$base/HospitalDocuments/$route" $null
        $data = Convert-JsonSafe $response.Body
        $empty = @($data.items).Count -eq 0 -or $null -eq $data.items
        Add-Result ("busca e contagens respeitam ACL: " + $route) ($warm.Code -eq 200 -and $response.Code -eq 200 -and $data.success -eq $true -and $empty -and -not $data.totalDocuments -and -not $data.totalResults) "HTTP $($response.Code)"
    }
    $deniedHistory = Invoke-App $denied.Session 'GET' "$base/HospitalDocuments/ReviewHistory?documentId=$documentId" $null
    $deniedJson = Convert-JsonSafe $deniedHistory.Body
    $blocked = $deniedHistory.Code -in 403, 404 -and -not $deniedHistory.Location
    Add-Result 'usuario sem acesso nao le o historico' $blocked ("HTTP $($deniedHistory.Code) destino=$($deniedHistory.Location)")
} else { Add-Skipped 'usuario sem acesso' 'o login nao foi comprovado; credencial invalida nao substitui o cenario de autorizacao' }

$other = Login-Profile $env:INOVAGED_AI_HTTP_OTHER_TENANT_EMAIL $env:INOVAGED_AI_HTTP_OTHER_TENANT_PASSWORD $env:INOVAGED_AI_HTTP_OTHER_TENANT_SLUG
Add-Result 'outro tenant autentica' $other.Authenticated $other.Detail
if ($other.Authenticated) {
    $foreign = Invoke-App $other.Session 'GET' "$base/HospitalDocuments/ReviewHistory?documentId=$documentId" $null
    $foreignJson = Convert-JsonSafe $foreign.Body
    $isolated = $foreign.Code -in 403, 404 -and -not $foreign.Location
    Add-Result 'outro tenant nao le o documento' $isolated ("HTTP $($foreign.Code) destino=$($foreign.Location)")
} else { Add-Skipped 'isolamento de tenant' $other.Detail }

$secrecy = Login-Profile $env:INOVAGED_AI_HTTP_SECRECY_EMAIL $env:INOVAGED_AI_HTTP_SECRECY_PASSWORD
Add-Result 'perfil de sigilo autentica' $secrecy.Authenticated $secrecy.Detail
if (-not $env:INOVAGED_AI_HTTP_FIXTURE_DATABASE) {
    Add-Skipped 'cenarios negativos e sigilo' 'exigem a fixture descartavel para mutacoes controladas'
} else {
    foreach ($case in @(
        @{Name='execucao de outro usuario';Sql="user_id='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003'";Code=404},
        @{Name='resultado expirado';Sql="result_expires_at=now()-interval '1 day'";Code=409},
        @{Name='fonte malformada';Sql="source_documents='[null]'::jsonb";Code=422}
    )) {
        $suggestion = New-MetadataSuggestion $editor.Session $token
        $safeExecution = ([guid]$suggestion.executionId).ToString()
        Invoke-FixtureSql "update ged.ai_execution set $($case.Sql) where id='$safeExecution';" | Out-Null
        $response = Apply-FixtureMetadata $editor.Session $token $suggestion
        Add-Result $case.Name ($response.Code -eq $case.Code) "HTTP $($response.Code)"
        Assert-NoApplication $case.Name $safeExecution
    }
    $suggestion = New-MetadataSuggestion $editor.Session $token
    $response = Apply-FixtureMetadata $editor.Session $token $suggestion @{versionId=[guid]::NewGuid().ToString()}
    Add-Result 'versao incompativel' ($response.Code -eq 409) "HTTP $($response.Code)"
    Assert-NoApplication 'versao incompativel' $suggestion.executionId

    $suggestion = New-MetadataSuggestion $editor.Session $token
    Invoke-FixtureSql "update ged.document set description='Edicao concorrente ficticia' where id='$documentId';" | Out-Null
    $response = Apply-FixtureMetadata $editor.Session $token $suggestion
    Add-Result 'edicao concorrente' ($response.Code -eq 409) "HTTP $($response.Code)"
    Assert-NoApplication 'edicao concorrente' $suggestion.executionId

    $originalOcr = Invoke-FixtureSql "select ocr_text from ged.document_search where document_id='$documentId';"
    try {
        Invoke-FixtureSql "update ged.document_search set ocr_text='' where document_id='$documentId';" | Out-Null
        $response = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/SuggestMetadata" @{versionId=$versionId;idempotencyKey=[guid]::NewGuid().ToString();__RequestVerificationToken=$token}
        Add-Result 'ausencia de OCR' ($response.Code -eq 422) "HTTP $($response.Code)"
        Invoke-FixtureSql "update ged.document_search set ocr_text='Texto ficticio sem evidencia para campos.' where document_id='$documentId';" | Out-Null
        $suggestion = New-MetadataSuggestion $editor.Session $token
        Add-Result 'sugestao insuficiente explicita' (@($suggestion.fields | Where-Object sufficient -eq $true).Count -eq 0) 'Nenhum campo tem suporte na fonte'
        Assert-NoApplication 'sugestao insuficiente' $suggestion.executionId
    } finally {
        $escapedOcr = $originalOcr.Replace("'", "''")
        Invoke-FixtureSql "update ged.document_search set ocr_text='$escapedOcr' where document_id='$documentId';" | Out-Null
    }
    $suggestion = New-MetadataSuggestion $editor.Session $token
    $response = Apply-FixtureMetadata $editor.Session $token $suggestion @{titleSet='false';isConfidentialSet='true';isConfidential='true';confidentialityJustification='Teste ficticio de autorizacao'}
    Add-Result 'sigilo sem permissao especifica' ($response.Code -eq 403) "HTTP $($response.Code)"
    Assert-NoApplication 'sigilo negado' $suggestion.executionId
    if ($secrecy.Authenticated) {
        $secrecyToken = Get-Token $secrecy.Session $viewerUrl
        $suggestion = New-MetadataSuggestion $secrecy.Session $secrecyToken
        try {
            $response = Apply-FixtureMetadata $secrecy.Session $secrecyToken $suggestion @{titleSet='false';isConfidentialSet='true';isConfidential='true';confidentialityJustification='Revisao humana ficticia'}
            $persisted = Invoke-FixtureSql "select is_confidential from ged.document where id='$documentId';"
            Add-Result 'sigilo com permissao especifica persiste' ($response.Code -eq 200 -and $persisted -eq 't') "HTTP $($response.Code) sigilo=$persisted"
        } finally { Invoke-FixtureSql "update ged.document set is_confidential=false where id='$documentId';" | Out-Null }
    } else { Add-Skipped 'sigilo permitido' $secrecy.Detail }
    $suggestion = New-MetadataSuggestion $editor.Session $token
    $nextVersion = [guid]::NewGuid().ToString()
    try {
        Invoke-FixtureSql "insert into ged.document_version(id,tenant_id,document_id,version_number,file_name,file_extension,file_size_bytes,storage_path,content_type) select '$nextVersion',tenant_id,document_id,2,file_name,file_extension,file_size_bytes,storage_path,content_type from ged.document_version where id='$versionId'; update ged.document set current_version_id='$nextVersion' where id='$documentId';" | Out-Null
        $response = Apply-FixtureMetadata $editor.Session $token $suggestion
        Add-Result 'mudanca de versao vigente' ($response.Code -eq 409) "HTTP $($response.Code)"
        Assert-NoApplication 'mudanca de versao vigente' $suggestion.executionId
    } finally {
        Invoke-FixtureSql "update ged.document set current_version_id='$versionId' where id='$documentId'; delete from ged.document_version where id='$nextVersion';" | Out-Null
    }
    if ($env:INOVAGED_AI_HTTP_CLASS_ID) {
        $response = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/SuggestArchivalClassification" @{versionId=$versionId;idempotencyKey=[guid]::NewGuid().ToString();__RequestVerificationToken=$token}
        $suggestion = Convert-JsonSafe $response.Body
        if ($response.Code -ne 200 -or -not $suggestion.executionId) { throw 'Sugestao de plano obrigatoria falhou.' }
        $nextPlan = [guid]::NewGuid().ToString()
        try {
            Invoke-FixtureSql "insert into ged.classification_plan_version(id,tenant_id,version_no,title) values('$nextPlan','00000000-0000-0000-0000-000000000001',2,'Plano ficticio sem a classe anterior');" | Out-Null
            $response = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyArchivalClassification" @{documentId=$documentId;versionId=$versionId;executionId=[string]$suggestion.executionId;concurrencyToken=[string]$suggestion.concurrencyToken;classificationId=$env:INOVAGED_AI_HTTP_CLASS_ID;__RequestVerificationToken=$token}
            Add-Result 'mudanca de plano vigente' ($response.Code -eq 400) "HTTP $($response.Code)"
            Assert-NoApplication 'mudanca de plano vigente' $suggestion.executionId
        } finally { Invoke-FixtureSql "delete from ged.classification_plan_version where id='$nextPlan';" | Out-Null }
    } else { Add-Skipped 'mudanca de plano' 'classe ficticia ausente' }
}
. (Join-Path $PSScriptRoot 'document-ai-protocol-http.ps1')
if ($RequireRealProviders) { Add-Skipped 'Groq, Gemini e DeepSeek' 'escopo solicitado; chamadas reais e evidencias por provedor ainda sao obrigatorias' }
else { Write-Output 'FORA DO ESCOPO | provedores reais | use -RequireRealProviders para exigir esta homologacao' }
if ($RequireVisual) { Add-Skipped 'verificacao visual' 'escopo solicitado; requer observacao de teclado, foco, contraste e larguras desktop/estreita' }
else { Write-Output 'FORA DO ESCOPO | verificacao visual | use -RequireVisual para exigir esta homologacao' }

if ($failed) { exit 1 }
if ($incomplete) { Write-Output 'NAO EXECUTADO: a matriz completa do ciclo HTTP nao foi percorrida.'; exit 2 }
exit 0
