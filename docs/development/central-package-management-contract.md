# Contrato de gerenciamento central de pacotes

`CentralPackageManagementContractTests` carrega `Directory.Packages.props` e todos os projetos com `XDocument`. O contrato rejeita `PackageVersion` duplicado, referência com atributo `Version`, referência com elemento filho `Version` e referência sem versão central.

Diretórios gerados (`bin`, `obj`, `artifacts` e `node_modules`) são ignorados. Para adicionar uma dependência, registre uma única versão em `Directory.Packages.props` e mantenha apenas `<PackageReference Include="Nome" />` no projeto.
