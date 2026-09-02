# Labels Logo Rendering Fix + Physical Movements Schema Fix + Premium Label UX

## Erro visual e causa encontrada

A etiqueta emitia uma URL autenticada pela política administrativa. Usuários autorizados a imprimir, mas sem a política de administração, recebiam uma resposta de autorização no `src` e o navegador exibia somente o texto alternativo. A impressão também dependia dessa requisição HTTP e, portanto, falhava fora do contexto da sessão.

## Correção da rota da imagem

`GET /Administration/BrandAssets/{id}/File` continua autenticada, valida o tenant e o estado `ACTIVE`, normaliza o caminho exclusivamente a partir de `storage_relative_path`, impede escape do `wwwroot` e devolve 404 quando o registro ou arquivo não existe. A administração da biblioteca permanece protegida pela política administrativa; a leitura do binário é permitida a qualquer usuário autenticado para que as etiquetas possam carregá-lo.

## ResolvedPrintLogo, preview e impressão

`ResolvedPrintLogo` agora separa `LogoUrl` (rota web controlada) de `PrintImageSource`. O novo `IPrintLogoImageSourceBuilder` valida novamente ativo/tenant e incorpora arquivos de até 1 MB como data URI. Acima desse limite usa a rota autenticada. A partial compartilhada prefere a fonte de impressão e não cria `<img>` quando nenhuma fonte válida foi resolvida. Preview, impressão de fábrica e os modelos LocDesk continuam consumindo o mesmo resolvedor e a mesma partial.

## PrintWizard e UX

O assistente apresenta o fluxo como: **O que imprimir**, **Modelo da etiqueta**, **Logo e identidade visual** e **Conferência final**. Selecionar um card de logo define explicitamente a origem `SELECTED`, atualiza a miniatura sem `src` vazio e apresenta logo, tamanho, posição, calibração, cópias, QR Code e histórico na conferência. A biblioteca usa cards com arte real, metadados e ações explícitas; o branding por modelo mantém dimensões, encaixe e teste de impressão.

## Physical/Movements

As consultas de caixas deixaram de presumir `box_code`. O serviço consulta `information_schema` e escolhe, em ordem, `box_code`, `box_no`, `code` ou `id::text`. A expressão controlada é usada em caixas, movimentos, empréstimos, inventário e leitura por scanner, sem aceitar SQL do usuário.

## History

History não foi alterado nesta entrega.

## Como validar

1. Envie PNG/JPG/WEBP e confirme o card na biblioteca.
2. Como usuário autenticado de etiquetas, abra a rota `File` do próprio tenant; teste também ID inexistente e ID de outro tenant (404).
3. Selecione a logo no PrintWizard e confirme no POST `LogoSelection=SELECTED` e `SelectedLogoAssetId`.
4. Inspecione a saída: `src` deve ser `data:image/...` (até 1 MB) ou `/Administration/BrandAssets/{id}/File`, nunca caminho físico, `~/` ou vazio.
5. Teste Pasta, Caixa, HOL, preview e impressão real, verificando proporção.
6. Abra `/Physical/Movements` em schemas com `box_no` e com `box_code`.

## Pendências

A impressão real, autenticação e acesso ao PostgreSQL exigem ambiente integrado com dados de tenant. Use o checklist de QA antes da promoção.
