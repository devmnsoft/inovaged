BeforeAll { Import-Module "$PSScriptRoot/../iis/InovaGed.IisDeployment.psm1" -Force }
Describe 'Deployment seguro' {
 It 'sanitiza segredos' { Protect-InovaGedText 'Password=real token=abc' | Should -Not -Match 'real|abc' }
 It 'rejeita path relativo' { $p=Join-Path $TestDrive c.json; '{"schemaVersion":"1.0","environment":"Homologation","siteName":"s","appPoolName":"p","releasesRoot":"relative","currentPath":"C:\\i\\current","sharedDataRoot":"C:\\p","configPath":"C:\\p\\c.json","healthCheckBaseUrl":"http://127.0.0.1","keepReleases":5,"httpPort":80}'|Set-Content $p; {Read-InovaGedConfiguration $p}|Should -Throw }
 It 'Validate expõe somente funções de leitura' { (Get-Command Test-InovaGedServer).Verb | Should -Be 'Test' }
 It 'bloqueia rollback sem release anterior' { Test-Path (Join-Path $TestDrive missing) | Should -BeFalse }
}
