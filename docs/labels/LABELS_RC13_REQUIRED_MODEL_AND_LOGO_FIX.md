# Labels RC13 — Required model, logo e impressão

## Incidente e causa

O runtime compiler do Razor emitia `CS0619` porque `LocDeskBoxLabel.cshtml` criava
`LocDeskLabelRenderModel` diretamente, enquanto o tipo expunha membros `required`.
Views agora apenas renderizam modelos completos; a criação e os fallbacks ficam na
`LocDeskLabelDemoFactory` e no controller.

## Correções

- `LocDeskLabelRenderModel` usa defaults seguros e uma logo vazia tipada.
- Caixa, pasta e HOL recebem modelos completos. A lista de cópias da caixa é
  preparada antes da view e não há construção de view model em Razor.
- `_PrintLogo` recebe somente `PrintLogoViewModel` e só emite `<img>` quando
  `PrintImageSource` é uma Data URI `data:image/` carregada.
- `_LocDeskLogo` apenas encaminha o mesmo view model para a partial compartilhada.
- Preview, visualização de impressão e impressão usam o construtor único do
  controller. `SelectedLogoAssetId` tem prioridade sobre vínculo do template e
  logo padrão do tenant.
- `BrandAssetImageService` valida tenant/status, normaliza o caminho sob
  `wwwroot`, lê o arquivo e produz a Data URI. Arquivo ausente gera aviso e não
  uma imagem quebrada.
- Os três botões do assistente fazem POST explícito. As páginas de etiqueta e a
  página de impressão oferecem **Imprimir agora**, ligado a `window.print()`.
- O assistente mantém configuração e conferência em duas colunas, com seleção,
  preview, estado, dimensões, proporção, encaixe e posição da logo.

## Validação

```bash
dotnet run --project InovaGed.Environment.Doctor -- labels-razor-logo-quality
dotnet build InovaGed.sln -v:minimal
```

Na validação manual, abra `/Labels/PrintWizard`, teste os modelos
`LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_V1` e `LOCDESK_PASTA_HOL_V1`, selecione uma
logo e confirme no HTML do preview e da impressão que o `src` começa com
`data:image/`. Em seguida use **Imprimir agora** e confirme a abertura do diálogo.
