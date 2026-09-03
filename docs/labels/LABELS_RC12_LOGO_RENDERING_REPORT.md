# Labels Uploaded Logo Rendering RC12 — relatório

## Diagnóstico

| Verificação | Resultado |
| --- | --- |
| Logo aparece na biblioteca? | **Sim.** A biblioteca usa o endpoint autenticado de arquivo. |
| Logo aparece no seletor do PrintWizard? | **Sim.** O dropdown é preenchido apenas com assets ativos do tenant. |
| Nome real do campo HTML da logo | `SelectedLogoAssetId`. |
| `SelectedLogoAssetId` chega no POST Preview? | **Sim.** O select pertence ao formulário principal e a action recebe `LabelPrintWizardInputModel`. |
| `SelectedLogoAssetId` chega no POST PrintPreview? | **Sim.** |
| `SelectedLogoAssetId` chega no POST Print? | **Sim.** |
| O controller chama `ILabelPrintLogoResolver`? | **Sim.** Preview, PrintPreview e Print passam pelo construtor único, inclusive LocDesk. |
| O resolver retorna `PrintImageSource` começando com `data:image/`? | **Sim**, quando o asset ativo possui arquivo legível; nunca devolve URL de arquivo como fonte de impressão. |
| A view recebe `PrintLogoViewModel`? | **Sim.** O resultado interno é mapeado antes de chegar ao Razor. |
| A partial `_PrintLogo` renderiza com `PrintImageSource`? | **Sim.** |
| O HTML final contém `data:image/`? | **Sim**, para logo carregada. Se a leitura falhar, nenhum `<img>` é emitido. |

## Causa real encontrada

O caminho de modelos personalizados saía do POST comum por um `RedirectToAction` para o editor LocDesk. Esse desvio remontava a tela e impedia Preview, PrintPreview e Print de compartilharem a mesma resolução final. Além disso, um valor antigo `LogoSelection=NONE` podia ganhar prioridade sobre um `SelectedLogoAssetId` explicitamente enviado.

## Correção aplicada

- O select simples tipado envia `SelectedLogoAssetId` no formulário principal.
- O asset explicitamente selecionado tem prioridade sobre todos os fallbacks.
- O fluxo LocDesk agora carrega seus dados e continua dentro de `BuildLabelRenderModelAsync`, sem redirect intermediário.
- `LabelPrintLogoResolver` usa `IBrandAssetImageService`, que valida tenant/status, normaliza o caminho sob `wwwroot`, lê os bytes e produz Data URI base64.
- A partial só emite `<img class="print-brand-logo">` quando a carga teve sucesso e a fonte começa com `data:image/`; falhas aparecem em alerta fora da etiqueta.
- Testes e o comando Doctor cobrem a fonte embutida e o cenário de arquivo ausente.

## Validação manual

A inspeção ponta a ponta em navegador requer banco com tenant, usuário e asset carregado e, portanto, não foi executada neste ambiente de build. O roteiro de aceite deve ser realizado em homologação para os três modelos: LocDesk Pasta, Caixa e HOL.
