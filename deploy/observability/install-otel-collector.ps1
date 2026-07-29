param([switch]$Approved)
$ErrorActionPreference='Stop'
if (-not $Approved) { throw 'Installation requires explicit -Approved. This script never configures credentials.' }
Write-Host 'Use the organization-approved Collector package and otel-collector.example.yaml; automatic production installation is intentionally disabled.'
