# Filtros do GED Workspace 2.0

## Disponíveis

Os filtros de texto livre, status OCR, classificação, dado sensível e período operam no conjunto carregado da pasta. A busca principal continua consultando o backend.

## Evoluções pendentes

- Pasta e tipo documental: a navegação pela árvore e a classificação rápida já cobrem esses fluxos; a consolidação em um único filtro depende de paginação no servidor.
- Temporalidade e pendências: dependem da inclusão dos estados no read model do explorer.
- Etiqueta: o controle é apresentado de forma segura e informa que a sincronização é necessária; o histórico de impressão ainda não integra o read model do documento.
- Para conjuntos maiores que a página carregada, os filtros devem ser promovidos a parâmetros do endpoint antes de serem considerados globais.
