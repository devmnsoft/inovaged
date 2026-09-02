# Labels Uploaded Logo Propagation Fix RC9

## Diagnóstico obrigatório

| Verificação | Resultado |
|---|---|
| A logo aparece na biblioteca? | **Sim.** O upload, a rota de arquivo e a listagem já estavam operacionais. |
| A logo aparece no seletor do PrintWizard? | **Sim.** Assets ativos do tenant são carregados no seletor. |
| O campo `SelectedLogoAssetId` existe no formulário? | **Sim.** |
| O campo está dentro do formulário principal? | **Sim.** |
| O POST `/Labels/Preview` recebe `SelectedLogoAssetId`? | **Sim.** O tag helper usa exatamente o nome do InputModel. |
| O POST `/Labels/Print` recebe `SelectedLogoAssetId`? | **Sim.** Ambos os botões submetem o mesmo form. |
| O método compartilhado chama `ILabelPrintLogoResolver`? | **Sim.** Preview e Print passam pelo pipeline compartilhado `BuildLabelRenderModelAsync`, que resolve a imagem uma vez. |
| O ViewModel da etiqueta recebe `PrintLogo`? | **Sim.** Views de fábrica recebem o contrato via `ViewBag.PrintLogo`; Pasta, Caixa e HOL recebem `LocDeskLabelRenderModel.PrintLogo`. |
| A partial da etiqueta usa `Model.PrintLogo`? | **Sim.** `_LocDeskLabel` e HOL encaminham essa propriedade para `_PrintLogo`. |
| O HTML final contém `src="data:image/..."`? | **Sim, quando o arquivo ativo existe.** `_PrintLogo` só emite `img` com a Data URI resolvida. |

## Causa real encontrada

A seleção explícita dependia também de `LogoSelection == "SELECTED"`. Assim, um POST com `SelectedLogoAssetId` válido, mas com a origem visual desatualizada, descartava o ID e caía no vínculo do template ou logo padrão. Além disso, os nomes de largura, altura e proporção do wizard divergiam do contrato RC9, e `_LocDeskLogo` ainda construía um fallback de URL hardcoded, sujeito a 404/autorização.

## Correção aplicada

- `SelectedLogoAssetId` agora tem prioridade absoluta no resolvedor; depois vêm vínculo do template, padrão do tenant e ausência de logo.
- O InputModel e o formulário usam os nomes RC9: `LogoWidthMm`, `LogoHeightMm`, `PreserveAspectRatio`, `LogoFitMode`, `LogoPosition`, `LogoOffsetXmm` e `LogoOffsetYmm`.
- O carregador aceita assets ativos do tenant ou de sistema, mantém isolamento entre tenants, normaliza o caminho sob `wwwroot` e fornece Data URI.
- `_LocDeskLogo` deixou de criar imagem hardcoded e apenas encaminha um `PrintLogoViewModel` resolvido para `_PrintLogo`.
- A partial de impressão não renderiza `img` sem `HasLogo`, `ImageLoaded` e `PrintImageSource` não vazio.
- O preview pequeno usa a imagem realmente escolhida e nunca mantém um `src` vazio.
- O Environment Doctor ganhou o comando `labels-logo-propagation` e verifica todo o contrato estático do RC9.

## Limite da validação local

A abertura do diálogo nativo de impressão e a confirmação de um asset real exigem banco, tenant autenticado e navegador no ambiente de homologação. O fluxo deve ser concluído manualmente seguindo os 15 passos do roteiro RC9; o quality gate cobre os contratos que podem ser validados sem esses recursos.
