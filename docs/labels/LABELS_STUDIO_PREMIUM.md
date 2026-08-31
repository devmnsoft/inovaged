# Labels Studio Premium — Galeria, Preview, Modelo e Impressão

## Objetivo
Centralizar a comparação, seleção e validação dos modelos de etiqueta sem persistir os dados demonstrativos.

## Rotas
- `GET /Labels/Templates`: galeria.
- `GET /Labels/Templates/{code}`: detalhe e metadados.
- `GET /Labels/Templates/{code}/Preview`: prévia oficial.
- `GET /Labels/Templates/{code}/PrintSample`: amostra com layout limpo.
- `POST /Labels/Templates/{code}/SetDefault`: seleção protegida por antiforgery.

## Modelos disponíveis
`FACTORY_BOX_V1`, `FACTORY_DOCUMENT_V1`, `LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_V1` e `LOCDESK_PASTA_HOL_V1`.

## Galeria e detalhe de modelo
Cards CSS apresentam miniatura, origem, modo, badges e atalhos. O detalhe registra view Razor, versão, dimensões, capacidades e campos esperados.

## Preview e impressão de amostra
O preview usa dados seguros em memória. A amostra usa `_PrintLayout`, sem navegação ou comandos. O CSS de impressão remove decoração de tela.

## LocDesk padrão e LocDesk HOL
O cabeçalho histórico `ARQUIVO LOCDESCK ANANINDEUA` foi preservado. HOL utiliza contrato Hosp. Ophir Loyola, controle vermelho, estrutura tabular e localização destacada.

## CSS criado
`labels-studio.css` concentra galeria, detalhes, palco de preview, amostra e responsividade. Os estilos existentes `labels-premium.css`, `labels-print.css` e `locdesk-label.css` permanecem responsáveis pelas telas operacionais.

## Validação visual
Validar galeria em desktop/mobile, os cinco previews, amostra sem chrome, editor LocDesk e impressão física calibrada.

## Pendências futuras
- Permitir padrão persistente por tenant quando a política de negócio for homologada.
- Adicionar snapshots visuais por navegador e impressora homologada.
