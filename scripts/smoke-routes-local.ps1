param(
    [string]$BaseUrl = "https://localhost:7282"
)

$routes = @(
    "/Administration",
    "/Administration/Users",
    "/Administration/Tenants",
    "/Labels/PrintWizard",
    "/RetentionDestination",
    "/Instruments/Versions/PCD",
    "/SchemaHealth"
)

$failed = 0
foreach ($route in $routes) {
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl$route" -UseBasicParsing -SkipCertificateCheck -MaximumRedirection 0 -ErrorAction Stop
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
