# Parte da matriz local: executar por dot-source a partir de document-ai-http.ps1.
# Usa somente IDs ficticios e as sessoes autenticadas pelo runner principal.
if (-not $secrecy.Authenticated -or -not $env:INOVAGED_AI_HTTP_FIXTURE_DATABASE) {
    Add-Skipped 'Protocolo institucional' 'perfil gestor autenticado e banco descartavel sao obrigatorios'
    return
}
$protocolId = 'ffffffff-ffff-ffff-ffff-ffffffffff01'
$sectorId = 'ffffffff-ffff-ffff-ffff-ffffffffff02'
$foreignProtocolId = 'ffffffff-ffff-ffff-ffff-ffffffffff03'
$foreignSectorId = 'ffffffff-ffff-ffff-ffff-ffffffffff04'
$managerId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb003'
function Assert-ProtocolDenied([string]$Name, $Response) {
    # Uma resposta 302 so conta quando a sessao ja foi comprovada e o destino
    # e exatamente AccessDenied; login, pagina inicial e redirect generico falham.
    $accessDenied = $false
    if ($Response.Code -eq 302 -and $Response.Location) {
        $targetUri = [uri]::new([uri]$base, [string]$Response.Location)
        $accessDenied = $targetUri.Authority -eq ([uri]$base).Authority -and $targetUri.AbsolutePath -eq '/Account/AccessDenied'
    }
    Add-Result $Name (($Response.Code -in 403,404) -or $accessDenied) "HTTP $($Response.Code) destino=$($Response.Location) sessaoGestor=$($secrecy.Authenticated)"
}
Invoke-FixtureSql @"
insert into ged.protocolo_setor(id,tenant_id,nome,sigla) values
('$sectorId','00000000-0000-0000-0000-000000000001','Setor ficticio HTTP','FIX-HTTP'),
('$foreignSectorId','00000000-0000-0000-0000-000000000002','Outro setor ficticio HTTP','FIX-OTHER') on conflict(id) do nothing;
insert into ged.protocolo(id,tenant_id,numero,assunto,setor_origem_id,setor_atual_id) values
('$protocolId','00000000-0000-0000-0000-000000000001','FIX-PROTO-HTTP','Processo ficticio HTTP','$sectorId','$sectorId'),
('$foreignProtocolId','00000000-0000-0000-0000-000000000002','FIX-OTHER-HTTP','Processo de outro tenant','$foreignSectorId','$foreignSectorId') on conflict(id) do nothing;
insert into ged.protocolo_usuario_setor(id,tenant_id,usuario_id,setor_id)
values('ffffffff-ffff-ffff-ffff-ffffffffff05','00000000-0000-0000-0000-000000000001','$managerId','$sectorId') on conflict(id) do nothing;
"@ | Out-Null
$protocolUrl = "$base/Protocolo/Ged/Vinculos/$protocolId"
$protocolToken = Get-Token $secrecy.Session $protocolUrl
$page = Invoke-App $secrecy.Session 'GET' $protocolUrl $null
Add-Result 'Protocolo autorizado e seletor GED' ($page.Code -eq 200 -and $protocolToken -and $page.Body.Contains($documentId) -and $page.Body.Contains('FIX-PROTO-HTTP')) "HTTP $($page.Code)"
if (-not $protocolToken) { Add-Skipped 'comandos de vinculo institucional' 'pagina autorizada sem antiforgery'; return }
$command = @{ProtocoloId=$protocolId;GedDocumentId=$documentId;TipoVinculo='VINCULO';__RequestVerificationToken=$protocolToken}
$initialCount = Invoke-FixtureSql "select count(*) from ged.protocolo_documento_ged where protocolo_id='$protocolId' and reg_status='A';"
try {
    Invoke-FixtureSql "update ged.protocolo_usuario_setor set ativo=false where id='ffffffff-ffff-ffff-ffff-ffffffffff05';" | Out-Null
    Assert-ProtocolDenied 'gestor sem vinculo de setor nao consulta' (Invoke-App $secrecy.Session 'GET' $protocolUrl $null)
    Assert-ProtocolDenied 'gestor sem vinculo de setor nao vincula' (Invoke-App $secrecy.Session 'POST' "$base/Protocolo/Ged/Vincular" $command)
    $count = Invoke-FixtureSql "select count(*) from ged.protocolo_documento_ged where protocolo_id='$protocolId' and reg_status='A';"
    Add-Result 'negacao de setor nao grava vinculo' ($count -eq $initialCount) "antes=$initialCount depois=$count"
} finally {
    Invoke-FixtureSql "update ged.protocolo_usuario_setor set ativo=true where id='ffffffff-ffff-ffff-ffff-ffffffffff05';" | Out-Null
}
Assert-ProtocolDenied 'ID de protocolo de outro tenant nao amplia acesso' (Invoke-App $secrecy.Session 'GET' "$base/Protocolo/Ged/Vinculos/$foreignProtocolId" $null)
try {
    Invoke-FixtureSql "update ged.document_acl set can_read=false,can_write=false where document_id='$documentId' and user_id='$managerId';" | Out-Null
    $page = Invoke-App $secrecy.Session 'GET' $protocolUrl $null
    Add-Result 'seletor nao expoe GED sem ACL' ($page.Code -eq 200 -and -not $page.Body.Contains($documentId)) "HTTP $($page.Code)"
    Assert-ProtocolDenied 'gestor do protocolo sem ACL GED nao vincula' (Invoke-App $secrecy.Session 'POST' "$base/Protocolo/Ged/Vincular" $command)
} finally {
    Invoke-FixtureSql "update ged.document_acl set can_read=true,can_write=true where document_id='$documentId' and user_id='$managerId';" | Out-Null
}
$response = Invoke-App $secrecy.Session 'POST' "$base/Protocolo/Ged/Vincular" $command
$linkId = Invoke-FixtureSql "select id from ged.protocolo_documento_ged where protocolo_id='$protocolId' and ged_document_id='$documentId' and reg_status='A' order by created_at desc limit 1;"
$validLink = $linkId -match '^[a-f0-9-]{36}$'
Add-Result 'vinculo autorizado persistido' ($response.Code -eq 302 -and $response.Location -like "*/Protocolo/Ged/Vinculos/$protocolId*" -and $validLink) "HTTP $($response.Code) vinculo=$linkId"
if ($validLink) {
    $remove = @{id=$linkId;protocoloId=$protocolId;__RequestVerificationToken=$protocolToken}
    try {
        Invoke-FixtureSql "update ged.document_acl set can_write=false where document_id='$documentId' and user_id='$managerId';" | Out-Null
        Assert-ProtocolDenied 'revogacao de edicao impede remover vinculo' (Invoke-App $secrecy.Session 'POST' "$base/Protocolo/Ged/RemoverVinculo" $remove)
        $state = Invoke-FixtureSql "select reg_status from ged.protocolo_documento_ged where id='$linkId';"
        Add-Result 'remocao negada preserva vinculo' ($state -eq 'A') "estado=$state"
    } finally {
        Invoke-FixtureSql "update ged.document_acl set can_write=true where document_id='$documentId' and user_id='$managerId';" | Out-Null
    }
    $response = Invoke-App $secrecy.Session 'POST' "$base/Protocolo/Ged/RemoverVinculo" $remove
    $state = Invoke-FixtureSql "select g.reg_status||'|'||d.reg_status from ged.protocolo_documento_ged g join ged.document d on d.id=g.ged_document_id where g.id='$linkId';"
    $audits = Invoke-FixtureSql "select count(*) from ged.protocolo_auditoria where entidade_id='$linkId' and usuario_id='$managerId' and acao in ('GED_VINCULO','GED_VINCULO_REMOVIDO');"
    Add-Result 'remocao autorizada preserva documento e audita comandos' ($response.Code -eq 302 -and $state -eq 'E|A' -and $audits -eq '2') "HTTP $($response.Code) estados=$state auditorias=$audits"
}
