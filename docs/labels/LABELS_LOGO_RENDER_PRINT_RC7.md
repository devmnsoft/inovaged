# Labels Logo Render & Print Action Fix RC7

## Problema encontrado

O HTML da etiqueta criava o elemento `img`, mas a fonte de impressão podia voltar a ser uma URL autenticada quando o arquivo ultrapassava 1 MB. Isso deixava preview e impressão dependentes de rota, cookie e contexto do navegador e produzia o ícone de imagem quebrada quando o arquivo físico não era localizado.

## Causa da imagem quebrada

O resolvedor considerava a logo disponível se houvesse apenas uma URL web. Ele não distinguia “asset selecionado” de “bytes carregados” e o partial podia renderizar a URL mesmo sem uma imagem válida. O caminho de upload também era resolvido por mais de um componente.

## Correção aplicada

`IBrandAssetImageService` centraliza a consulta isolada por tenant e status `ACTIVE`, normaliza somente caminhos relativos sob `wwwroot`, lê os bytes e produz `data:<content-type>;base64,...`. PNG, JPEG e WEBP são os únicos tipos aceitos. Falhas retornam `null` e geram warning sem revelar o caminho físico.

`ResolvedPrintLogo` agora separa `HasLogo`, `ImageLoaded` e `LoadError`. O partial só cria `img` quando a imagem foi efetivamente carregada. O aviso fica fora da etiqueta.

## Como a logo é salva

O upload valida extensão, MIME, assinatura binária e limite, usa nome aleatório e grava um caminho relativo por tenant em `wwwroot/uploads/branding`. O banco armazena metadados e nunca fornece um caminho absoluto ao cliente.

## Como a logo é carregada e vira Data URI

O serviço consulta `ged.brand_asset` por `assetId` e `tenantId`, exige registros ativos, impede path traversal, lê os bytes e converte todo o arquivo permitido para Base64. Não há fallback para rota autenticada no conteúdo imprimível.

## Preview e impressão

Os dois POSTs passam por `ProcessWizard`, que resolve uma única instância de `ResolvedPrintLogo`. A prioridade é seleção explícita, vínculo do template e padrão do tenant. LocDesk Pasta, Caixa e HOL recebem o mesmo modelo de renderização.

## Botão imprimir

As páginas possuem **Imprimir agora** com `data-label-print-now`. `labels-print-page.js` registra o clique diretamente e chama `window.print()`, sem popup ou `fetch`. A regra de mídia remove toolbar, navegação e alertas da folha real.

## Melhorias de UX

O assistente mantém origem, modelo, identidade e conferência em cards; a biblioteca apresenta arte e metadados; as páginas imprimíveis têm toolbar consistente, retorno para edição e aviso explicativo fora da etiqueta. O diagnóstico de cada asset informa disponibilidade do arquivo e da Data URI sem expor o disco.

## Como validar

1. Enviar PNG em **Logos e Marcas** e abrir Arquivo/Diagnóstico.
2. Selecioná-lo no PrintWizard e gerar a prévia.
3. Confirmar no DOM que a etiqueta contém `src="data:image/png;base64,..."`.
4. Gerar impressão e clicar em **Imprimir agora**.
5. Conferir Pasta, Caixa e HOL em escala 100%, sem deformação ou ícone quebrado.
6. Executar o teste `LabelsLogoRenderingContractTests` e o build da solução.

## Pendências

A abertura e inspeção do diálogo nativo de impressão exige navegador e usuário autenticado; deve ser registrada no QA manual do ambiente homologado.
