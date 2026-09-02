# Seleção e redimensionamento de logo externa

## Objetivo
Permitir que cada tenant envie a arte oficial e a selecione para etiquetas ou documentos, sem reconstruir a marca com texto ou CSS.

## Upload de logo
Em **Administração > Logos**, informe empresa, nome e arquivo PNG, JPG/JPEG ou WEBP (máximo de 5 MB). SVG é bloqueado. O servidor valida extensão, MIME e assinatura, usa nome físico aleatório e calcula SHA-256; caminhos físicos nunca são apresentados.

## Seleção de logo
`/Labels/Branding` vincula uma logo do próprio tenant a cada modelo, sem entrada manual de identificador. No PrintWizard, escolha o padrão do modelo, padrão do cliente, uma logo cadastrada ou nenhuma logo.

## Redimensionamento em mm
A edição aceita largura de 10–90 mm e altura opcional de 5–60 mm, com preview real. `CONTAIN` é o padrão; `COVER` pode cortar e `FILL` exibe alerta de possível deformação.

## Preservação de proporção
A opção vem habilitada. A partial `_PrintLogo` sempre produz um elemento `img` e usa `object-fit`, sem filtros ou alteração de cor.

## Aplicação em etiquetas
Os vínculos abrangem `LOCDESK_PASTA_V1`, `LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_HOL_V1`, `FACTORY_BOX_V1` e `FACTORY_DOCUMENT_V1`. Preview e impressão consomem a mesma URL protegida.

## Aplicação em documentos
Cabeçalhos imprimíveis compartilham `_PrintableDocumentHeader` e `_PrintLogo`; perfis de branding resolvem a arte por cliente/contexto.

## PrintWizard e LocDesk
O assistente apresenta somente logos ativas do tenant, preview clicável e ajustes físicos. O fluxo LocDesk preserva número de controle, volume, campos e QR Code.

## Segurança
Todas as rotas internas exigem autorização; POSTs usam antiforgery. Consultas e arquivos são isolados por `tenant_id`, nomes são gerados pelo servidor e SVG não é aceito.

## Como validar
Aplique `database/apply_all_required_migrations.sql`, abra as telas de logos, envie PNG/JPG, edite as dimensões, vincule os cinco modelos e compare preview e impressão em escala 100%.

## Pendências futuras
Leitura de dimensões de todos os codecs no upload e recorte interativo poderão ser adicionados sem alterar o armazenamento da arte original.
