# Relatório do hotfix 04.1.27-R3

## Escopo e causas

- CS0246/CS1061: tipos e método de snapshot do runner JavaScript foram usados como se pertencessem ao Playwright .NET.
- CS0411: a chamada sem resultado a `EvaluateAllAsync<T>` não fornecia tipo inferível.
- NU1008: o metapacote `OpenTelemetry` não obedecia ao gerenciamento central.

## Alterações

`BrowserTestMatrix` agora falha no CI sem URL, estabiliza conteúdo com `EvaluateAsync`, captura o PNG real, compara-o com o golden e só então registra o manifesto. `VisualSnapshotAssert` implementa tamanho/SHA-256 e bloqueia atualizações no CI. Foram acrescentados contratos unitários para o comparador, superfície Playwright e CPM. O job `ui-tests-build` restaura, compila, instala Chromium, inicia banco e Web, aguarda readiness, executa a matriz, valida o manifesto e publica evidências.

## Validação e operação

Foram executadas as buscas estáticas das APIs inválidas, a validação XML tipada equivalente ao contrato de CPM e `git diff --check`, todas sem violações. Os comandos obrigatórios de clean, limpeza do cache NuGet, restore, builds e testes foram invocados, mas o ambiente do Codex não possui o executável `dotnet` (`exit 127`); portanto, esses resultados dependem da execução do job de CI. A validação local do YAML também ficou limitada pela ausência do módulo Python `yaml`.

O baseline só é atualizado localmente com `INOVAGED_UPDATE_UI_BASELINES=true` e requer revisão explícita. A instalação do Chromium não foi tentada, pois o projeto não pôde ser compilado neste ambiente, conforme a ordem de operação exigida.

## Riscos e rollback

A comparação binária é sensível a qualquer diferença de renderização; imagens devem ser geradas no ambiente Linux controlado usado no CI. O rollback consiste em reverter os commits deste hotfix; não se deve restaurar a API inexistente nem colocar versões em projetos.
