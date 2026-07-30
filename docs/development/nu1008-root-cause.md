# Causa raiz do NU1008

O repositório habilita Central Package Management em `Directory.Packages.props`. O projeto de infraestrutura referenciava o metapacote `OpenTelemetry` sem uma entrada central correspondente; variantes anteriores também associavam uma versão diretamente ao `PackageReference`, o que produz NU1008.

O metapacote não era uma dependência direta necessária. Ele foi removido, enquanto os cinco pacotes específicos de exporter, hosting e instrumentação permanecem centralizados e alinhados em `1.9.0`. Nenhum `PackageReference` de projeto deve declarar atributo ou elemento filho `Version`.
