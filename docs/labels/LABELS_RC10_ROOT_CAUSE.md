# Labels PrintWizard RC10 — diagnóstico de causa raiz

## Evidências antes da correção

| Verificação | Resultado |
|---|---|
| A logo aparece na biblioteca BrandAssets? | **Sim**, a biblioteca utiliza a action autenticada `BrandAssets/{id}/File`. A confirmação visual em uma instalação com banco deve integrar a homologação manual. |
| A logo aparece no bloco “Logo oficial cadastrada”? | **Sim**, o PrintWizard já recebia os ativos ativos em `ViewBag.BrandAssets` e montava o preview pela URL da biblioteca. |
| Qual é o `name` do campo select da logo no HTML? | **`SelectedLogoAssetId`**, gerado por `asp-for="SelectedLogoAssetId"`. |
| `SelectedLogoAssetId` chega no POST `/Labels/Preview`? | **Sim**, o campo está no form e o input da action tem a propriedade correspondente. O RC10 adiciona log explícito e teste estático para impedir regressão. |
| `SelectedLogoAssetId` chega no POST `/Labels/Print`? | **Sim**, pelo mesmo binding; o RC10 registra somente a presença do identificador, nunca o base64. |
| Existe mais de um form na página? | **Não**, existe um único form principal. |
| Os botões estão dentro do form principal? | **Sim**. O form agora possui o id estável `label-print-form`; as ações do painel também o referenciam explicitamente. |
| Os botões têm type correto? | **Parcialmente antes**: Preview e Print eram submit, mas não havia a terceira ação PrintPreview. **Sim após RC10**: os três são `type="submit"`. |
| Os botões têm formaction correto? | **Parcialmente antes**: só Preview e Print. **Sim após RC10**: Preview, PrintPreview e Print apontam para POSTs reais. |
| O ViewModel da etiqueta recebe PrintLogo? | **Sim** nas variantes LocDesk; nas views de fábrica o mesmo `PrintLogoViewModel` é disponibilizado pelo controller. |
| A partial da etiqueta usa `Model.PrintLogo`? | **Sim**, `_LocDeskLabel` e HOL encaminham `Model.PrintLogo` para a partial compartilhada. |
| O src final do img dentro da etiqueta começa com `data:image/`? | **Sim após RC10**, e a partial recusa qualquer outro esquema. |
| Se não começa, qual src foi renderizado? | Antes, a interface cliente podia copiar `/Administration/BrandAssets/{id}/File` para o mock da etiqueta, e a partial aceitava qualquer string não vazia. Uma URL inacessível/ausente deixava o navegador exibir o ícone quebrado e o alt. Agora nenhum `<img>` é emitido se a fonte resolvida não for Data URI válida. |

## Causa real encontrada

Dois fluxos com contratos diferentes estavam misturados: o preview da biblioteca dependia de uma rota HTTP, enquanto a impressão precisava ser autocontida. A proteção Razor verificava apenas string não vazia, não o contrato `data:image/`. Além disso, o PrintWizard oferecia somente Preview e Print; não existia POST explícito de PrintPreview, e não havia um identificador estável no form para associar ações do painel lateral.

## Correção aplicada

O serviço lê o arquivo autorizado sob `IWebHostEnvironment.WebRootPath`, normaliza o caminho e gera Data URI. `_PrintLogo` só emite `<img>` quando `HasLogo`, `ImageLoaded` e uma fonte `data:image/` estão válidos. As três actions registram a presença de `SelectedLogoAssetId` e usam `BuildLabelRenderModelAsync`. O formulário e os dois conjuntos de ações usam submit/formaction nativos, sem depender de JavaScript ou popup; todas as páginas impressas conservam o fallback inline de `window.print()`.

> A inspeção acima é do fluxo e dos contratos do repositório. A confirmação dos dados de uma instalação específica requer tenant, banco, arquivo cadastrado e sessão autenticada.
