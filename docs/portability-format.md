# Formato de portabilidade InovaGED

- `manifest.json`: versão do formato, export id, tenant id, escopo, status, correlation id e lista de arquivos.
- `checksums.sha256`: uma linha por arquivo exportado, com SHA-256 e caminho relativo.
- Caminhos absolutos, traversal, duplicados, links simbólicos/reparse points e arquivos extras invalidam o pacote.
- Segredos, hashes de senha, tokens e connection strings não devem ser exportados.

## Estrutura canônica Evolução 03.1

```text
inovaged-export/
├── README.txt
├── manifest.json
├── checksums.sha256
├── tenant/
│   └── tenant.json
├── metadata/
│   ├── folders.ndjson
│   ├── documents.ndjson
│   └── document-versions.ndjson
├── documents/
│   └── {document-id}/versions/{version-id}/arquivo.ext
└── reports/
    └── inconsistencies.csv
```

O pacote não deve conter senhas, hashes de senha, tokens, cookies, sessões, strings de conexão, chaves privadas, segredos JWT, credenciais de integração ou chaves de criptografia.
