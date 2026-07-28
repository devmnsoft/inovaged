# Pacotes

`Directory.Packages.props` é a fonte central de versões. `PackageReference` nos projetos não contém `Version`. Atualize intencionalmente o arquivo central, execute restore sem `--locked-mode`, revise locks e auditorias (`dotnet list InovaGed.sln package`, `--include-transitive`, `--outdated` e `--vulnerable`) e então volte ao restore bloqueado. Não faça upgrades indiscriminados.
