# Labels Pixel Perfect RC3 — Fidelidade Visual, LocDesk, HOL e Impressão Real

## Objetivo
Consolidar a apresentação física das etiquetas sem alterar regras de negócio: dimensões em milímetros, tipografia sóbria, contraste, alinhamento de campos e paridade entre prévia e impressão.

## Modelos revisados
- `LOCDESK_PASTA_V1` — prioridade máxima.
- `LOCDESK_CAIXA_V1` — prioridade máxima.
- `LOCDESK_PASTA_HOL_V1` — prioridade máxima.
- `FACTORY_BOX_V1` e `FACTORY_DOCUMENT_V1` — conferência na Revisão Visual.

## Antes/depois textual
Antes, estilos de impressão estavam repetidos, a pasta padrão usava dimensão diferente da declarada no catálogo e a HOL tinha sombra de card. Depois, os três modelos LocDesk têm superfície branca, borda preta, medidas físicas estáveis e um único contrato de impressão.

## Correções aplicadas
### LocDesk padrão e caixa
Pasta e caixa foram alinhadas a 174 × 110 mm. Controle e volume permanecem vermelhos, valores longos usam quebra segura e a localização recebe contorno preto.

### LocDesk HOL
O texto **ARQUIVO LOCDESCK ANANINDEUA** foi preservado. O exemplo usa contrato Hosp. Ophir Loyola, controle 199 e todos os campos arquivísticos solicitados. A amostra é criada apenas em memória e não persiste dados.

### QR Code
A área reservada mantém fundo branco, dimensão em milímetros e TraceCode próximo. O payload continua sendo URL/token de rastreio, sem expor identificadores técnicos no texto da etiqueta.

### Preview
`/Labels/VisualReview` reúne cinco modelos com nome, código, status e atalhos para prévia, amostra e qualidade. `/Labels/VisualChecklist` registra os onze critérios por modelo.

### Impressão
`labels-print.css` concentra `@page` e `@media print`, oculta navegação e ações, remove fundo/sombra e aplica `break-inside: avoid` e ajuste exato de cores.

## CSS revisado
Foram revisados `locdesk-label.css`, `locdesk-labels.css`, `labels-print.css` e os estilos premium. `labels-visual-review.css` contém somente a apresentação Atlas das duas telas internas.

## Arquivos prioritários ausentes
`Views/Labels/BatchPreview.cshtml` não existe; o fluxo equivalente usa `BatchPrint.cshtml`. Todos os demais arquivos prioritários informados existem.

## Pendências visuais
A validação definitiva de deslocamento depende da impressora, mídia adesiva e driver do cliente. O item QR da caixa permanece como **Atenção** até leitura em papel; isso não indica falha funcional.

## Como validar
1. Execute `dotnet run --project InovaGed.Environment.Doctor -- labels-visual-quality`.
2. Abra a Central, Revisão Visual e Checklist Visual.
3. Gere as três amostras LocDesk e imprima em escala 100%, sem cabeçalhos/rodapés do navegador.
4. Meça 174 × 110 mm, confira borda, controle/volume, localização e leia o QR em dispositivo físico.
