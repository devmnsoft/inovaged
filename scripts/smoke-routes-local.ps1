param(
    [string]$BaseUrl = "https://localhost:7282"
)

$routes = @(
    "/Administration",
    "/Administration/Users",
    "/Administration/Tenants",
    "/Administration/Security",
    "/Administration/Migrations",
    "/Administration/DesignSystem",
    "/DatabaseReadiness",
    "/SystemIncidents",
    "/Labels",
    "/Labels/Templates",
    "/Labels/Templates/FACTORY_BOX_V1",
    "/Labels/Templates/FACTORY_DOCUMENT_V1",
    "/Labels/Templates/LOCDESK_CAIXA_V1",
    "/Labels/Templates/LOCDESK_PASTA_V1",
    "/Labels/Templates/LOCDESK_PASTA_HOL_V1",
    "/Labels/Templates/LOCDESK_PASTA_HOL_V1/Preview",
    "/Labels/Templates/LOCDESK_PASTA_HOL_V1/PrintSample",
    "/Labels/Print",
    "/Labels/PrintWizard",
    "/Labels/History",
    "/Labels/LocDesk",
    "/Ged",
    "/SmartGed",
    "/SmartWorkflow",
    "/RetentionDestination",
    "/Instruments/Versions/PCD",
    "/SchemaHealth"
)

$failed = 0
foreach ($route in $routes) {
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl$route" -UseBasicParsing -SkipCertificateCheck -MaximumRedirection 0 -ErrorAction Stop
        $body = [string]$response.Content
        if ($response.StatusCode -ge 500 -or $body -match '42703|42P01|DatabaseSchemaException não tratada|An item with the same key has already been added|Unknown Atlas icon|Ícone Atlas desconhecido') {
            throw "Falha de compatibilidade de schema detectada ($($response.StatusCode))."
        }
        Write-Host "$route -> $($response.StatusCode)"
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -eq 401 -or $status -eq 403 -or $status -eq 302) {
            Write-Host "$route -> $status esperado/autorização"
        }
        else {
            Write-Host "$route -> FAIL $($_.Exception.Message)" -ForegroundColor Red
            $failed++
        }
    }
}

if ($failed -gt 0) { exit 1 }
