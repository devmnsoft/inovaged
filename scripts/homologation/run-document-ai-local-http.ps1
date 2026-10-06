$ErrorActionPreference = 'Stop'
$env:INOVAGED_AI_HTTP_BASE = 'http://127.0.0.1:5189'
$profiles = @{ EDITOR='arquivistaophir'; READER='hospital'; SECRECY='administradorophir'; DENIED='denied'; OTHER_TENANT='other' }
foreach ($profile in $profiles.Keys) {
    [Environment]::SetEnvironmentVariable("INOVAGED_AI_HTTP_${profile}_EMAIL", $profiles[$profile] + '@inovaged.local')
    [Environment]::SetEnvironmentVariable("INOVAGED_AI_HTTP_${profile}_PASSWORD", 'Ficticio-local-2026!')
}
$env:INOVAGED_AI_HTTP_EDITOR_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb004'
$env:INOVAGED_AI_HTTP_DOCUMENT_ID = 'dddddddd-dddd-dddd-dddd-dddddddddd01'
$env:INOVAGED_AI_HTTP_VERSION_ID = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeee01'
$env:INOVAGED_AI_HTTP_OTHER_TENANT_SLUG = 'fixture-other'
$env:INOVAGED_AI_HTTP_CLASS_ID = 'cccccccc-cccc-cccc-cccc-cccccccccc01'
$env:INOVAGED_AI_HTTP_FIXTURE_DATABASE = (Get-Content (Join-Path $PSScriptRoot '../../artifacts/ai-operational/install-database.txt')).Trim()
& (Join-Path $PSScriptRoot 'document-ai-http.ps1')
exit $LASTEXITCODE
