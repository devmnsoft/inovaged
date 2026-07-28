# Manifesto
`database/migrations.manifest.json` é a fonte canônica da ordem. IDs são únicos, caminhos são relativos à raiz e `transactional` controla uma transação por migration. O Migrator calcula SHA-256: checksum igual é ignorado; divergência ou falha anterior bloqueia a execução.
