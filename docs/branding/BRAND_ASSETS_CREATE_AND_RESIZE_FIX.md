# BrandAssets Runtime Fix + Logo Selector Resize UX

## Erro encontrado e causa

A action `BrandAssetsController.Create` usava a descoberta convencional, mas as telas estavam em `Views/Administration/BrandAssets`; por isso o MVC procurava `Views/BrandAssets/Create.cshtml` e retornava erro em runtime. Todas as actions agora informam consistentemente o caminho administrativo explícito.

## Rotas e telas corrigidas

As rotas administrativas de listagem, criação, detalhes, edição, arquivo, definição de padrão e arquivamento permanecem protegidas. Os aliases legados `/BrandAssets` e `/BrandAssets/Create` apontam para as mesmas actions. `/status` e `/Home/Status` fornecem o status público esperado sem interferir na página de status HTTP parametrizada.

## Upload, redimensionamento e segurança

O upload aceita PNG, JPEG e WEBP até 5 MB, verifica extensão, MIME e assinatura, usa nome físico GUID, SHA-256 e caminho relativo isolado pelo tenant. SVG não sanitizado é rejeitado. A criação e edição aceitam largura/altura em milímetros, preservação de proporção e `CONTAIN`, `COVER` ou `FILL`; `CONTAIN` e proporção preservada são os padrões.

A rota de arquivo valida tenant e registro ativo, resolve o arquivo somente sob `wwwroot` e não revela o caminho físico. POSTs usam antiforgery e consultas sempre filtram o tenant.

## Seleção por modelo, PrintWizard e LocDesk

`/Labels/Branding` configura a logo por template sem entrada manual de UUID. O PrintWizard oferece configuração do modelo, padrão do cliente, logo cadastrada ou ausência de logo e expõe dimensões e encaixe. As etiquetas LocDesk e previews reutilizam a partial `Branding/_PrintLogo`, que emite uma imagem real; a prioridade é seleção da impressão, configuração do template, padrão do tenant e, por fim, nenhuma logo.

## Como validar

1. Aplicar as migrations obrigatórias.
2. Abrir `/Administration/BrandAssets/Create`, enviar PNG/JPEG e conferir o preview.
3. Editar dimensões, proporção, encaixe e posição.
4. Associar a logo aos templates em `/Labels/Branding`.
5. Conferir PrintWizard, previews LocDesk e impressão.
6. Confirmar respostas de `/status` e `/Home/Status`.

## Pendências futuras

Sanitização de SVG pode ser adicionada em uma entrega separada. Testes visuais com impressoras físicas e perfis de papel específicos continuam fazendo parte da homologação operacional.
