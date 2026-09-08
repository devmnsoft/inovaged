# Esquema JSON do Label Canvas RC22

`schemaVersion` começa em `1`. Valores geométricos usam milímetros; tipografia usa pontos.

```json
{
  "schemaVersion": 1,
  "canvas": {
    "widthMm": 100,
    "heightMm": 70,
    "paper": "A4",
    "orientation": "portrait",
    "gridMm": 2,
    "safeMarginMm": 3
  },
  "elements": [
    {
      "id": "title",
      "type": "text",
      "name": "Título",
      "xMm": 5,
      "yMm": 5,
      "widthMm": 70,
      "heightMm": 8,
      "rotationDeg": 0,
      "zIndex": 10,
      "locked": false,
      "visible": true,
      "text": "ARQUIVO LOCDESCK ANANINDEUA",
      "style": {
        "fontFamily": "Arial",
        "fontSizePt": 10,
        "fontWeight": "700",
        "align": "center",
        "color": "#111111",
        "backgroundColor": "transparent",
        "border": "none",
        "borderRadiusMm": 0,
        "paddingMm": 1,
        "wrap": true
      },
      "binding": null,
      "validation": {
        "required": true,
        "maxCharacters": 80,
        "showOnlyWhenValue": false
      }
    }
  ],
  "bindings": {
    "subjectType": "LocDeskFolder",
    "sampleDataProfile": "HOL"
  }
}
```

Tipos aceitos: `text`, `field`, `qr`, `barcode`, `logo`, `image`, `line`, `rect`, `group`, `table` e `separator`. `binding.field` deve existir em `/Labels/Designer/Fields?subjectType=...`. `binding.asset` aceita somente caminho absoluto interno iniciado por `/` ou data URI de imagem base64 válida. `src=""`, `data:,` e `/data:,` nunca são emitidos.

Campos desconhecidos podem ser preservados por clientes futuros, mas são ignorados pelo renderizador RC22. IDs devem ser únicos dentro do template. Publicações persistem exatamente o JSON validado e seu SHA-256.
