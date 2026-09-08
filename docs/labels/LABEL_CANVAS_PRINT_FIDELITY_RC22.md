# Fidelidade de impressão do Label Canvas RC22

## Unidades e folha

O HTML final aplica `mm` diretamente em largura, altura, posição, padding e raio. Fontes usam `pt`. A regra `@page` usa A4 retrato e margem de 5 mm. Etiquetas com até 100 mm de largura usam grade de duas colunas; etiquetas largas, como LocDesk/HOL de 174 × 110 mm, usam uma coluna. Cada cópia é uma seção independente com `break-inside: avoid`.

## Calibração

Use o fluxo de calibração existente em `/Labels/Calibration`. Ajustes X/Y selecionados no assistente são incluídos no snapshot de impressão. Em impressoras térmicas ou drivers com escala automática, desative “Ajustar à página” e imprima a 100%.

## Logo e imagens

O renderizador aceita asset interno ou data URI de imagem base64 válida. Origem ausente usa fallback textual controlado. Nunca passe uma data URI por `Url.Content`. Se a logo não aparecer, verifique:

1. se o asset existe e é acessível ao tenant;
2. se o MIME é `image/*` e o base64 não está vazio;
3. se largura e altura não estão fora do canvas;
4. se o elemento está visível e em camada adequada.

## QR Code e rastreio

No preview, `qrPayload` usa dados de amostra. Na impressão real o fluxo registra a emissão, obtém o código de rastreio e renderiza novamente com a URL autorizada. O snapshot registra `template_key`, versão, SHA-256, assunto, usuário, modo, canal e `layoutSource=CANVAS`.

## Critérios de aceitação

- medir uma linha de 100 mm com tolerância definida pelo dispositivo;
- verificar margem segura e recorte;
- validar contraste e fonte mínima de 6 pt;
- ler QR/código de barras no suporte final;
- testar uma e duas etiquetas por A4;
- confirmar que reimpressão exige justificativa e conserva o snapshot.
