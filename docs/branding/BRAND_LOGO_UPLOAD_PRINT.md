# Brand Logo Upload for Print

## Objetivo
Cadastrar a imagem oficial de cada cliente e reutilizá-la, sem redesenho, em etiquetas e cabeçalhos imprimíveis.

## Rotas
`/Administration/BrandAssets` lista, envia, visualiza, define padrão e arquiva. `/Labels/Branding` vincula assets aos modelos; `/Labels/Branding/TestPrint` gera a prova.

## Banco de dados
A migration `2026_09_01_brand_logo_upload_print.sql` cria `brand_asset`, `print_brand_profile` e `print_template_brand_binding`, com índices idempotentes.

## Upload seguro e formatos aceitos
PNG, JPG/JPEG e WEBP até 5 MB (configurável em `Branding:MaxUploadBytes`). Extensão, MIME e magic bytes são conferidos; o conteúdo recebe nome GUID e SHA-256. SVG é bloqueado enquanto não houver sanitizador.

## Configuração
Na listagem, defina a logo padrão. Em **Marca das etiquetas**, escolha um asset visual para cada template. No PrintWizard, escolha padrão do modelo, padrão do cliente, outra logo ou nenhuma.

## Etiquetas e documentos
Use `Branding/_PrintLogo` em etiquetas e `Branding/_PrintableDocumentHeader` nos documentos. Ambos preservam proporção e apontam para a rota autenticada do asset, nunca para o caminho físico.

## Segurança e auditoria
Todas as rotas exigem autenticação/política administrativa; POSTs usam antiforgery e consultas validam o tenant. Eventos previstos: `BRAND_ASSET_UPLOADED`, `BRAND_ASSET_SET_DEFAULT`, `BRAND_ASSET_ARCHIVED`, `PRINT_BRAND_PROFILE_CREATED`, `PRINT_BRAND_PROFILE_UPDATED`, `LABEL_TEMPLATE_LOGO_BOUND`, `LABEL_PRINTED_WITH_BRAND_ASSET` e `DOCUMENT_PRINTED_WITH_BRAND_ASSET`.

## Como validar
Aplique migrations, envie cada formato permitido, teste SVG/arquivo adulterado, defina padrão, vincule os cinco templates e confira preview e impressão em escala real.

## Pendências futuras
Sanitização SVG, dimensões extraídas no servidor, CDN privada e expansão gradual do cabeçalho para todos os relatórios legados.
