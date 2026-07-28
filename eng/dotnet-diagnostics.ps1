$ErrorActionPreference = 'Continue'; $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path; $out = Join-Path $root 'artifacts/diagnostics'; New-Item $out -ItemType Directory -Force | Out-Null
function Invoke-Dotnet([string[]]$Arguments) { ((& dotnet @Arguments 2>&1) | Out-String).Trim() }
$projects = Get-ChildItem $root -Recurse -Filter *.csproj | Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]'
$tfms = @($projects | ForEach-Object { ([xml](Get-Content $_.FullName -Raw)).Project.PropertyGroup.TargetFramework } | Where-Object { $_ } | Sort-Object -Unique)
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = if (Test-Path $vswhere) { (& $vswhere -latest -property installationVersion | Out-String).Trim() } else { 'not detected' }
$data = [ordered]@{ os=[System.Environment]::OSVersion.ToString(); architecture=[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString(); dotnetRoot=$env:DOTNET_ROOT; dotnetInfo=Invoke-Dotnet @('--info'); sdks=Invoke-Dotnet @('--list-sdks'); runtimes=Invoke-Dotnet @('--list-runtimes'); selectedSdk=Invoke-Dotnet @('--version'); globalJson=(Join-Path $root 'global.json'); targetFrameworks=$tfms; visualStudio=$visualStudio; msbuildAndNuget='reported by dotnet --info' }
$data | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out 'dotnet-environment.json') -Encoding utf8
$data.GetEnumerator() | ForEach-Object { "{0}: {1}" -f $_.Key, ($_.Value -join ', ') } | Set-Content (Join-Path $out 'dotnet-environment.txt') -Encoding utf8
Write-Host "Diagnóstico seguro criado em $out"
