# Formato de portabilidade InovaGED

- `manifest.json`: versão do formato, export id, tenant id, escopo, status, correlation id e lista de arquivos.
- `checksums.sha256`: uma linha por arquivo exportado, com SHA-256 e caminho relativo.
- Caminhos absolutos, traversal, duplicados, links simbólicos/reparse points e arquivos extras invalidam o pacote.
- Segredos, hashes de senha, tokens e connection strings não devem ser exportados.
