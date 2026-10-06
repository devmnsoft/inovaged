# Dedicated test database only. Does not represent a complete installation.
[CmdletBinding()]
param([string]$Container = 'inovaged-ai-operational-pg')
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$sql = "create schema if not exists ged; create extension if not exists pgcrypto; create table if not exists ged.tenant(id uuid primary key, name text);`n"
foreach ($file in @(
    '2026_10_02_document_ai_governance.sql',
    '2026_10_03_document_ai_governance_hardening.sql',
    '2026_10_04_document_ai_execution_sources.sql',
    '2026_10_05_document_ai_application_integrity.sql',
    '2026_10_06_document_ai_review_identity_preflight.sql',
    '2026_10_06_document_ai_review_recovery.sql',
    '2026_10_07_document_ai_retention_retry.sql')) {
    $sql += "`nBEGIN;`n" + (Get-Content -LiteralPath (Join-Path $root "database/migrations/$file") -Raw) + "`nCOMMIT;`n"
}
$sql | docker exec -i $Container psql -X -v ON_ERROR_STOP=1 -U postgres -d inovaged_test
if ($LASTEXITCODE -ne 0) { throw 'Test schema preparation failed.' }
