# Estratégia de snapshots visuais

Cada caso grava o PNG atual em `InovaGed.UiTests/Screenshots/actual` e procura o baseline homônimo em `Screenshots/golden`. `VisualSnapshotAssert` lê o golden, compara o tamanho e o SHA-256 com os bytes retornados pelo Playwright e mantém o arquivo atual quando há divergência.

A comparação é deliberadamente binária e determinística. Uma divergência informa ambos os caminhos e hashes; não há tolerância de pixels escondida. Só depois da comparação bem-sucedida o teste valida o console e registra a execução no manifesto.
