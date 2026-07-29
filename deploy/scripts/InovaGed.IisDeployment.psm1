Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Protect-InovaGedText { param([string]$Text) $Text -replace '(?i)(password|pwd|token|client_secret)(\s*[=:]\s*)[^;\s\"}]+','$1$2***' -replace '(?i)(Host|Server)=[^;]+','$1=***' -replace '(?i)(User ID|Username)=[^;]+','$1=***' }
function Read-InovaGedConfiguration {
 param([Parameter(Mandatory)][string]$Path)
 if(-not (Test-Path -LiteralPath $Path -PathType Leaf)){ throw "Configuração não encontrada." }
 $c=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
 foreach($n in 'schemaVersion','environment','siteName','appPoolName','releasesRoot','currentPath','sharedDataRoot','configPath','healthCheckBaseUrl'){if([string]::IsNullOrWhiteSpace([string]$c.$n)){throw "Campo obrigatório inválido: $n"}}
 if($c.schemaVersion -ne '1.0' -or $c.environment -notin @('Homologation','Production') -or $c.keepReleases -lt 2 -or $c.keepReleases -gt 20 -or $c.httpPort -lt 1 -or $c.httpPort -gt 65535){throw 'Configuração de deployment inválida.'}
 foreach($p in 'releasesRoot','currentPath','sharedDataRoot','configPath'){if(-not [IO.Path]::IsPathFullyQualified([string]$c.$p)){throw "$p deve ser absoluto."}}
 $url=$null;if(-not [Uri]::TryCreate([string]$c.healthCheckBaseUrl,[UriKind]::Absolute,[ref]$url) -or $url.Scheme -notin @('http','https')){throw 'healthCheckBaseUrl inválida.'}
 if($c.currentPath.StartsWith($c.releasesRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'currentPath não pode estar dentro de releasesRoot.'}; return $c
}
function Test-InovaGedPackage { param([string]$PackagePath,[string]$ToolPath='InovaGed.Deployment.Tool.dll') if(-not(Test-Path $PackagePath)){throw 'Pacote não encontrado.'}; & dotnet $ToolPath verify-package --package $PackagePath; if($LASTEXITCODE){throw 'Pacote reprovado.'} }
function Write-InovaGedReport { param($Configuration,[string]$DeploymentId,[string]$Mode,[string]$Result,[array]$Events,[string]$ReleaseId='')
 $root=Join-Path $Configuration.sharedDataRoot "deployment/$DeploymentId"; New-Item $root -ItemType Directory -Force|Out-Null
 $report=[ordered]@{deploymentId=$DeploymentId;correlationId=[guid]::NewGuid().ToString();releaseId=$ReleaseId;actor='redacted';mode=$Mode;environment=$Configuration.environment;finishedAtUtc=[DateTime]::UtcNow.ToString('o');result=$Result;rollbackResult=$null;events=$Events}
 $safe=Protect-InovaGedText ($report|ConvertTo-Json -Depth 8); [IO.File]::WriteAllText((Join-Path $root 'deployment.json'),$safe); $safe|Out-File (Join-Path $root 'deployment.txt'); return $report
}
function Test-InovaGedServer { param($Configuration,[string]$PackagePath)
 $checks=@(); function Add($name,$ok,$blocking,$message){$script:checks += [pscustomobject]@{name=$name;passed=$ok;blocking=$blocking;message=$message}}
 Add Windows $IsWindows $true 'Windows Server é obrigatório para operações IIS.'; Add PowerShell ($PSVersionTable.PSVersion.Major-ge 7) $false 'PowerShell 7 recomendado.'
 Add Package (Test-Path $PackagePath) $true 'Pacote deve existir.'; Add Config (Test-Path $Configuration.configPath) $true 'Configuração externa deve existir.'
 Add WebAdministration ([bool](Get-Module -ListAvailable WebAdministration)) $true 'Módulo WebAdministration obrigatório.'
 Add Disk ((Get-PSDrive -Name ([IO.Path]::GetPathRoot($Configuration.releasesRoot).TrimEnd(':\\')) -ErrorAction SilentlyContinue).Free -gt 1GB) $true 'Mínimo de 1 GB livre.'
 [pscustomobject]@{ready=-not($checks|Where-Object{$_.blocking-and-not$_.passed});warnings=@($checks|Where-Object{-not$_.blocking-and-not$_.passed}).Count;checks=$checks}
}
function Switch-InovaGedRelease { [CmdletBinding(SupportsShouldProcess)]param($Configuration,[string]$ReleasePath)
 Import-Module WebAdministration; if($PSCmdlet.ShouldProcess($Configuration.siteName,"Alterar physicalPath para $ReleasePath")){Set-ItemProperty "IIS:\Sites\$($Configuration.siteName)" -Name physicalPath -Value $ReleasePath; Restart-WebAppPool $Configuration.appPoolName}
}
function Invoke-InovaGedHealthCheck { param([string]$BaseUrl,[int]$TimeoutSeconds=180) $until=(Get-Date).AddSeconds($TimeoutSeconds); do{try{$r=Invoke-WebRequest "$BaseUrl/health/ready" -UseBasicParsing -TimeoutSec 10;if($r.StatusCode-eq 200){return $true}}catch{};Start-Sleep 2}while((Get-Date)-lt$until); return $false }
function Remove-OldInovaGedReleases { [CmdletBinding(SupportsShouldProcess)]param($Configuration,[string[]]$ProtectedReleaseIds)
 Get-ChildItem $Configuration.releasesRoot -Directory|Sort-Object LastWriteTimeUtc -Descending|Select-Object -Skip $Configuration.keepReleases|Where-Object{$_.Name-notin$ProtectedReleaseIds}|ForEach-Object{if($PSCmdlet.ShouldProcess($_.FullName,'Remover release histórica')){Remove-Item $_.FullName -Recurse -Force}}
}
Export-ModuleMember -Function *-InovaGed*
