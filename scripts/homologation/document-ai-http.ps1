# Verificacao HTTP do ciclo documental assistido.
# Nao trata redirecionamento para login como autenticacao nem como autorizacao.
# Provedor Deterministic exige INOVAGED_AI_DETERMINISTIC=1 na aplicacao local.
# Nao homologa Groq, Gemini ou DeepSeek.
# Exit 0: passos executados passaram. Exit 1: falha. Exit 2: nao executado.

$ErrorActionPreference = 'Stop'
$base = $env:INOVAGED_AI_HTTP_BASE
if ([string]::IsNullOrWhiteSpace($base)) {
    Write-Output 'NAO EXECUTADO: defina INOVAGED_AI_HTTP_BASE com a URL local. Nao use producao.'
    exit 2
}
$required = @(
    'INOVAGED_AI_HTTP_EDITOR_EMAIL', 'INOVAGED_AI_HTTP_EDITOR_PASSWORD',
    'INOVAGED_AI_HTTP_READER_EMAIL', 'INOVAGED_AI_HTTP_READER_PASSWORD',
    'INOVAGED_AI_HTTP_SECRECY_EMAIL', 'INOVAGED_AI_HTTP_SECRECY_PASSWORD',
    'INOVAGED_AI_HTTP_DENIED_EMAIL', 'INOVAGED_AI_HTTP_DENIED_PASSWORD',
    'INOVAGED_AI_HTTP_OTHER_TENANT_EMAIL', 'INOVAGED_AI_HTTP_OTHER_TENANT_PASSWORD',
    'INOVAGED_AI_HTTP_DOCUMENT_ID', 'INOVAGED_AI_HTTP_VERSION_ID'
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
        try { $location = [string]$http.Headers['Location'] } catch { }
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
function Login-Profile([string]$Email, [string]$Password) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginPage = Invoke-App $session 'GET' "$base/Account/Login" $null $true
    $token = ([regex]::Match($loginPage.Body, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"')).Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($token)) { return @{ Authenticated = $false; Session = $session; Detail = 'token de login ausente' } }
    $posted = Invoke-App $session 'POST' "$base/Account/Login" @{ Email = $Email; Password = $Password; TenantSlug = 'default'; __RequestVerificationToken = $token } $true
    $landedOnLogin = $posted.Location -match '/Account/Login' -or ($posted.Code -eq 200 -and $posted.Body -match 'id="loginForm"')
    $home = Invoke-App $session 'GET' "$base/" $null $true
    $authenticated = -not $landedOnLogin -and -not $home.LoginRedirect -and $home.Body -notmatch 'id="loginForm"'
    return @{ Authenticated = $authenticated; Session = $session; Detail = "login=$($posted.Code) destino=$($posted.Location) inicio=$($home.Code) redirectLogin=$($home.LoginRedirect)" }
}
function Convert-JsonSafe([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $trim = $Text.Trim()
    if (-not $trim.StartsWith('{') -and -not $trim.StartsWith('[')) { return $null }
    return $trim | ConvertFrom-Json
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
if (-not $editor.Authenticated) { Write-Output 'NAO EXECUTADO: a sessao do editor nao foi comprovada. Os demais passos do ciclo foram interrompidos.'; exit 2 }

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
    Add-Result 'historico registra a decisao' ($after.Code -eq 200 -and $after.Total -eq ($before.Total + 1)) ("antes=$($before.Total) depois=$($after.Total)")
    $replay = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" @{ documentId = $documentId; versionId = $versionId; executionId = $executionId; concurrencyToken = '1'; titleSet = 'true'; title = $title; descriptionSet = 'false'; isConfidentialSet = 'false'; __RequestVerificationToken = $token }
    $replayJson = Convert-JsonSafe $replay.Body
    $replayOk = $replay.Code -eq 200 -and $null -ne $replayJson -and $replayJson.alreadyApplied -eq $true -and ($replayJson.retentionPending -eq $true -or $replayJson.message -match 'conclu')
    Add-Result 'repeticao idempotente' $replayOk ("HTTP $($replay.Code) $($replay.Body)")
    $afterReplay = Get-HistoryCount $editor.Session $documentId
    Add-Result 'repeticao nao aumenta o historico' ($afterReplay.Total -eq $after.Total) ("total=$($afterReplay.Total)")
    $conflict = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyMetadataSuggestion" @{ documentId = $documentId; versionId = $versionId; executionId = $executionId; concurrencyToken = $concurrency; titleSet = 'true'; title = ($title + ' outro'); descriptionSet = 'false'; isConfidentialSet = 'false'; __RequestVerificationToken = $token }
    $conflictJson = Convert-JsonSafe $conflict.Body
    Add-Result 'mesma revisao com outra decisao' ($conflict.Code -eq 409) ("HTTP $($conflict.Code) $($conflict.Body)")
    $afterConflict = Get-HistoryCount $editor.Session $documentId
    Add-Result 'conflito nao grava outra revisao' ($afterConflict.Total -eq $after.Total) ("total=$($afterConflict.Total)")
    if ($env:INOVAGED_AI_HTTP_CLASS_ID) {
        $archival = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/SuggestArchivalClassification" @{ versionId = $versionId; idempotencyKey = [guid]::NewGuid().ToString(); __RequestVerificationToken = $token }
        $archivalJson = Convert-JsonSafe $archival.Body
        $archivalOk = $archival.Code -eq 200 -and $null -ne $archivalJson -and $archivalJson.executionId
        Add-Result 'sugestao de classificacao arquivistica' $archivalOk ("HTTP $($archival.Code)")
        if ($archivalOk) {
            $classApply = Invoke-App $editor.Session 'POST' "$base/HospitalDocuments/ApplyArchivalClassification" @{ documentId = $documentId; versionId = $versionId; executionId = [string]$archivalJson.executionId; concurrencyToken = [string]$archivalJson.concurrencyToken; classificationId = $env:INOVAGED_AI_HTTP_CLASS_ID; __RequestVerificationToken = $token }
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
    Add-Result 'leitor nao aplica' ($readerApply.Code -in 302, 403, 404) ("HTTP $($readerApply.Code)")
} else { Add-Skipped 'leitor consulta e nao aplica' $reader.Detail }

$denied = Login-Profile $env:INOVAGED_AI_HTTP_DENIED_EMAIL $env:INOVAGED_AI_HTTP_DENIED_PASSWORD
Add-Result 'usuario sem acesso autentica' $denied.Authenticated $denied.Detail
if ($denied.Authenticated) {
    $deniedHistory = Invoke-App $denied.Session 'GET' "$base/HospitalDocuments/ReviewHistory?documentId=$documentId" $null
    $deniedJson = Convert-JsonSafe $deniedHistory.Body
    $blocked = $deniedHistory.LoginRedirect -or $deniedHistory.Code -in 302, 403, 404 -or ($null -ne $deniedJson -and $deniedJson.success -eq $false)
    Add-Result 'usuario sem acesso nao le o historico' $blocked ("HTTP $($deniedHistory.Code) destino=$($deniedHistory.Location)")
} else { Add-Skipped 'usuario sem acesso' 'o login nao foi comprovado; credencial invalida nao substitui o cenario de autorizacao' }

$other = Login-Profile $env:INOVAGED_AI_HTTP_OTHER_TENANT_EMAIL $env:INOVAGED_AI_HTTP_OTHER_TENANT_PASSWORD
Add-Result 'outro tenant autentica' $other.Authenticated $other.Detail
if ($other.Authenticated) {
    $foreign = Invoke-App $other.Session 'GET' "$base/HospitalDocuments/ReviewHistory?documentId=$documentId" $null
    $foreignJson = Convert-JsonSafe $foreign.Body
    $isolated = $foreign.Code -in 403, 404 -or ($foreign.Code -eq 302 -and $foreign.Location -notmatch '/Account/Login') -or ($null -ne $foreignJson -and $foreignJson.success -eq $false)
    Add-Result 'outro tenant nao le o documento' $isolated ("HTTP $($foreign.Code) destino=$($foreign.Location)")
} else { Add-Skipped 'isolamento de tenant' $other.Detail }

$secrecy = Login-Profile $env:INOVAGED_AI_HTTP_SECRECY_EMAIL $env:INOVAGED_AI_HTTP_SECRECY_PASSWORD
Add-Result 'perfil de sigilo autentica' $secrecy.Authenticated $secrecy.Detail
if (-not $secrecy.Authenticated) { Add-Skipped 'alteracao de sigilo' $secrecy.Detail }
else { Add-Skipped 'alteracao de sigilo sem permissao e com permissao' 'exige duas execucoes ficticias ja geradas e a conferencia do valor persistido no mesmo banco da aplicacao' }

Add-Skipped 'execucao de outro usuario, versao incompativel, resultado expirado, fonte malformada, mudanca de versao ou de plano, ausencia de OCR e sugestao insuficiente' 'estes cenarios exigem a aplicacao local no provedor Deterministic e mutacoes ficticias no banco descartavel. Nao foram executados nesta sessao.'
Add-Skipped 'Groq, Gemini e DeepSeek' 'homologacao remota exige a credencial e uma execucao real de cada provedor'
Add-Skipped 'verificacao visual do GED e do visualizador' 'nao ha ferramenta de navegador nesta sessao. Teclado, largura estreita e painel nao foram observados.'

if ($failed) { exit 1 }
if ($incomplete) { Write-Output 'NAO EXECUTADO: a matriz completa do ciclo HTTP nao foi percorrida.'; exit 2 }
exit 0
