# Labels RC18 — correção de Data URI

## Carregamento da logo

A seleção `SelectedLogoAssetId` atravessa as actions de preview e impressão até o resolvedor compartilhado. O asset é consultado no tenant, precisa estar ativo, ter tipo permitido e apontar para um arquivo seguro dentro de `wwwroot`.

`BrandAssetImageService` lê bytes não vazios, converte-os com `Convert.ToBase64String` e monta `data:<content-type>;base64,<conteúdo>`. A validação `ImageDataUriValidator.IsValidImageDataUri` exige o prefixo `data:image/`, o marcador `;base64,` e payload não vazio.

## Como o HTML evita `data:,`

`_PrintLogo.cshtml` recebe `PrintLogoViewModel`, valida `PrintImageSource` e só então renderiza a imagem. Não há fallback de URL, prefixo `/` nem passagem por `Url.Content`. Asset ausente, ilegível ou inválido resulta na ausência do elemento `<img>`, nunca em uma requisição HTTP para `/data:,`.

Preview, página de impressão e impressão registrada usam o mesmo método de montagem e o mesmo resolvedor. Os logs registram somente flags (`SelectedLogo`, `HasLogo`, `ImageLoaded` e `DataUri`), sem registrar o Base64.

## Validação do HTML

1. Selecione uma logo cadastrada em `/Labels/PrintWizard` e use **Pré-visualizar etiqueta**.
2. Inspecione o elemento e confirme `src="data:image/...;base64,..."`.
3. Confirme a ausência de `src="data:,"`, `src="/data:,"`, caminhos físicos e URLs de upload na logo impressa.
4. Use **Visualizar impressão**, confirme a mesma imagem e clique **Imprimir agora**.
5. Repita sem logo/arquivo válido e confirme que nenhum `<img class="print-brand-logo">` é produzido.
6. Execute o gate `dotnet run --project InovaGed.Environment.Doctor -- server-labels-iis-quality`.
