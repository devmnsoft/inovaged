# QA — fidelidade da logo LocDesk

## Objetivo

Eliminar toda reconstrução visual da marca e centralizar o uso da arte oficial, sem alterar proporção, cor, tipografia, símbolo ou espaçamento.

## Asset oficial utilizado

O único caminho aceito pela aplicação é `wwwroot/images/brands/locdesk/locdesk-logo-original.png`, centralizado por `BrandAssets`. O arquivo binário aprovado **não estava disponível neste repositório** durante a correção; ele deve ser copiado sem transformação para esse caminho. Até isso ocorrer, o partial usa o fallback técnico neutro e nenhuma logo aproximada é exibida.

## Telas revisadas

- `/Labels`, `/Labels/LocDesk`, `/Labels/Templates`, `/Labels/TemplateDetails` e `/Labels/PrintWizard`.
- `/Labels/Demo`, `/Labels/VisualReview` e `/Labels/History`.
- Previews e impressão de Pasta, Caixa e Pasta HOL.
- `DemoSamples` e `DemoAcceptance`.

## Views corrigidas

`LocDesk`, `Templates`, `TemplateDetails`, `PrintWizard`, `VisualReview`, `DemoSamples`, as três views físicas de etiqueta e `_LocDeskLabel` (por meio do partial compartilhado).

## Partials criados

`Views/Shared/_LocDeskLogo.cshtml` é o renderizador único.

## CSS aplicado

`wwwroot/css/locdesk-brand.css` controla somente tamanho máximo, altura automática e `object-fit: contain`. Não há desenho, filtro, recoloração ou transformação da marca.

## Checklist de fidelidade

- [ ] Logo oficial carregada — pendente da entrega do PNG original aprovado.
- [x] Sem SVG improvisado.
- [x] Sem CSS reconstruindo a marca.
- [x] Sem texto simulando a marca como solução principal.
- [ ] LocDesk Pasta usa logo correta — integração pronta; depende do PNG oficial.
- [ ] LocDesk Caixa usa logo correta — integração pronta; depende do PNG oficial.
- [ ] LocDesk HOL usa logo correta — integração pronta; depende do PNG oficial.
- [ ] Templates usam logo correta — integração pronta; depende do PNG oficial.
- [ ] Demo usa logo correta — integração pronta; depende do PNG oficial.
- [ ] VisualReview usa logo correta — integração pronta; depende do PNG oficial.
- [ ] Impressão usa logo correta — integração pronta; depende do PNG oficial.
- [ ] Preview usa logo correta — integração pronta; depende do PNG oficial.

## Pendências

1. O responsável pela marca deve fornecer o arquivo original aprovado e copiá-lo, sem conversão ou edição, como `locdesk-logo-original.png`.
2. Após a inclusão do binário, executar a inspeção visual comparativa em navegador e impressão física a 100% e marcar os itens pendentes acima.
