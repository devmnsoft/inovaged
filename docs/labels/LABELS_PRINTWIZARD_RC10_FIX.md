# Labels PrintWizard Final Fix RC10

## Problema encontrado

A biblioteca e o seletor conseguiam mostrar a logo por uma URL autenticada, mas o contrato da etiqueta não impedia que uma origem HTTP ou inválida chegasse ao `<img>`. O wizard também não apresentava as três transições de servidor de forma explícita.

## Causa dos botões não funcionarem

Faltava uma ação POST `PrintPreview` para o modelo do wizard e seu botão correspondente. A interface tinha apenas Preview e Print e misturava a conferência lateral com as ações no fim do formulário.

## Causa da logo não aparecer na etiqueta

A origem visual do seletor (`/Administration/BrandAssets/{id}/File`) não é apropriada para uma impressão autocontida. Embora o resolver já gerasse Data URI, a última barreira de apresentação aceitava qualquer origem não vazia, permitindo imagem quebrada em uma regressão ou arquivo ausente.

## Correção do form

O único formulário POST recebeu `id="label-print-form"`. `TemplateCode`, origem, `SelectedLogoAssetId`, dimensões, proporção, encaixe, posição, offsets e conferência continuam no mesmo formulário.

## Correção dos botões

Pré-visualizar etiqueta, Visualizar impressão e Imprimir são submits HTML nativos com `formaction` e `formmethod="post"`. Há ações equivalentes junto à conferência lateral. Não há popup, fetch ou dependência de JavaScript para navegar.

## Correção do SelectedLogoAssetId

O select permanece ligado exatamente a `SelectedLogoAssetId`. Preview, PrintPreview e Print escrevem log estruturado contendo apenas action, template e um booleano de presença do id.

## Correção do BuildLabelRenderModelAsync

As três actions encaminham o mesmo input ao método único. O método valida tenant/template/origem, resolve QR e logo, converte para `PrintLogoViewModel`, publica warning fora da etiqueta e devolve a view final.

## Correção do Data URI

`BrandAssetImageService` consulta somente ativo do tenant ou de sistema, normaliza `~/`, barra inicial, `wwwroot/` e separadores, impede caminho absoluto/traversal, resolve sob `WebRootPath`, verifica o arquivo e retorna `data:image/...;base64,...`.

## Correção do _PrintLogo

A partial tipada não renderiza `<img>` se a imagem não estiver carregada, a origem estiver vazia ou não começar por `data:image/`. `_LocDeskLogo` apenas delega à partial comum.

## Melhoria de design

O formulário ganhou status legível da imagem, tratamento de erro do preview sem ícone quebrado, ações com hierarquia clara em ambas as colunas, painel neutro mais espaçoso e layout responsivo equilibrado.

## Como validar

1. Abra Logos e Marcas e confirme a arte cadastrada.
2. Abra o PrintWizard, selecione origem, modelo e logo; confirme “Logo carregada”.
3. Submeta as três ações e procure `SelectedLogoAssetIdPresent=true` no log.
4. Inspecione a etiqueta e confirme que o `src` começa em `data:image/`.
5. Teste “Imprimir agora” nas páginas de fábrica, LocDesk Pasta, Caixa e HOL.
6. Execute `dotnet run --project InovaGed.Environment.Doctor -- labels-printwizard-actions` e o build da solução.

## Pendências

A homologação visual com arquivo real, banco do tenant, autenticação e diálogo nativo de impressão deve ser feita no ambiente integrado; o diálogo do navegador não pode ser automatizado com fidelidade por teste estático de backend.
