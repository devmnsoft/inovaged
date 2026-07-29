$ErrorActionPreference='Stop'
$config=Join-Path $PSScriptRoot 'otel-collector.example.yaml'
if (-not (Test-Path $config)) { throw 'Collector example not found.' }
if (Select-String -Path $config -Pattern '(?i)(token|authorization):\s*[^#\s]') { throw 'Potential credential in example.' }
Write-Output 'Collector example passes the offline safety contract.'
