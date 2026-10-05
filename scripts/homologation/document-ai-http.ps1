# Reproducible HTTP check for the document-AI journeys.
# Uses a local application and fictional identifiers only.
# A mock provider is not homologation of Groq, Gemini or DeepSeek.
# Exit 0: every requested step passed.
# Exit 1: a step failed.
# Exit 2: not executed (missing base URL or required fictional identifiers).

$ErrorActionPreference = 'Stop'
$base = $env:INOVAGED_AI_HTTP_BASE
if ([string]::IsNullOrWhiteSpace($base)) {
    Write-Output 'NAO EXECUTADO: defina INOVAGED_AI_HTTP_BASE com a URL local do aplicativo. Nao use producao, dump real ou segredo.'
    exit 2
}

$required = @(
    'INOVAGED_AI_HTTP_USER',
    'INOVAGED_AI_HTTP_PASSWORD',
    'INOVAGED_AI_HTTP_VERSION_ID',
    'INOVAGED_AI_HTTP_DOCUMENT_ID'
)
$missing = @($required | Where-Object { [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
if ($missing.Count -gt 0) {
    Write-Output ("NAO EXECUTADO: faltam variaveis ficticias: " + ($missing -join ', '))
    exit 2
}

function Get-AntiForgery {
    param($Session, [string]$Url)
    $page = Invoke-WebRequest -Uri $Url -WebSession $Session -UseBasicParsing
    $token = [regex]::Match($page.Content, '__RequestVerificationToken[^>]*value="([^"]+)"').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($token)) { throw "Token antiforgery ausente em $Url" }
    return $token
}

$results = New-Object System.Collections.Generic.List[string]
function Add-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    $state = $(if ($Passed) { 'PASSOU' } else { 'FALHOU' })
    $script:results.Add("$state | $Name | $Detail")
    Write-Output "$state | $Name | $Detail"
}

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginPage = "$base/Account/Login"
$loginToken = Get-AntiForgery -Session $session -Url $loginPage
$loginCode = 0
try {
    $login = Invoke-WebRequest -Uri $loginPage -WebSession $session -Method Post -UseBasicParsing -MaximumRedirection 0 -Body @{
        UserName = $env:INOVAGED_AI_HTTP_USER
        Password = $env:INOVAGED_AI_HTTP_PASSWORD
        __RequestVerificationToken = $loginToken
    }
    $loginCode = [int]$login.StatusCode
} catch {
    if ($_.Exception.Response) { $loginCode = [int]$_.Exception.Response.StatusCode }
}
Add-Result 'login do perfil ficticio' ($loginCode -in 200, 302) ("HTTP " + $loginCode)

$viewer = "$base/HospitalDocuments/Viewer?documentId=$($env:INOVAGED_AI_HTTP_DOCUMENT_ID)&versionId=$($env:INOVAGED_AI_HTTP_VERSION_ID)"
try {
    $pageToken = Get-AntiForgery -Session $session -Url $viewer
    $page = Invoke-WebRequest -Uri $viewer -WebSession $session -UseBasicParsing
    $hasType = $page.Content.Contains('Sugerir tipo documental')
    $hasArchival = $page.Content.Contains('Sugerir classificação arquivística') -or $page.Content.Contains('Sugerir classificacao arquivistica')
    $hasAskHint = $page.Content.Contains('ged.document_type')
    Add-Result 'telas de tipo documental e classificacao' ($hasType -and $hasArchival -and $hasAskHint) 'parcial do visualizador'
} catch {
    Add-Result 'telas de tipo documental e classificacao' $false $_.Exception.Message
    $pageToken = $null
}

if ($pageToken) {
    $applyCode = 0
    try {
        $apply = Invoke-WebRequest -Uri "$base/HospitalDocuments/ApplyMetadataSuggestion" -WebSession $session -Method Post -UseBasicParsing -Body @{
            documentId = $env:INOVAGED_AI_HTTP_DOCUMENT_ID
            versionId = $env:INOVAGED_AI_HTTP_VERSION_ID
            executionId = [guid]::Empty
            concurrencyToken = 0
            isConfidentialSet = 'true'
            isConfidential = 'false'
            __RequestVerificationToken = $pageToken
        }
        $applyCode = [int]$apply.StatusCode
    } catch {
        if ($_.Exception.Response) { $applyCode = [int]$_.Exception.Response.StatusCode }
    }
    $denied = $applyCode -in 400, 403, 404, 409, 302
    Add-Result 'aplicacao sem execucao correspondente' $denied ("HTTP " + $applyCode)
} else {
    Write-Output 'NAO EXECUTADO: aplicacao sem execucao, porque o visualizador nao devolveu token.'
}

$failed = @($results | Where-Object { $_.StartsWith('FALHOU') }).Count
if ($failed -gt 0) { exit 1 }
Write-Output 'HTTP executado somente contra a base informada. Provedores reais nao foram homologados por este roteiro.'
exit 0
