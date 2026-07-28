# Validação de integridade

A verificação rejeita arquivos `.partial`, arquivos ausentes, linhas de checksum malformadas, divergências SHA-256 comparadas em tempo constante, manifesto inválido e tamanho divergente. Os resultados possíveis são `VALID`, `INVALID` e `NOT_VERIFIABLE`, persistidos com achados estruturados. O provider real executa `pg_restore --list` antes de declarar sucesso.
