# Checklist QA — logo, impressão e UX de etiquetas RC7

| Verificação | Status | Observações |
|---|---|---|
| Logo aparece na biblioteca | ☐ | Testar PNG, JPEG e WEBP permitidos. |
| Logo aparece no preview | ☐ | Confirmar `data:image/...;base64` no DOM. |
| Logo aparece na página de impressão | ☐ | Usar a mesma seleção e dimensões. |
| Logo aparece na impressão real | ☐ | Validar papel/PDF em escala 100%. |
| Não há ícone quebrado | ☐ | Arquivo ausente deve gerar alerta fora da etiqueta. |
| Botão imprimir chama `window.print` | ✅ automatizado | Quality gate `labels-logo-rendering`. |
| PrintWizard está claro | ☐ | Validar fluxo guiado e conferência. |
| BrandAssets está claro | ☐ | Validar cards, metadados e diagnóstico. |
| Labels/Branding está claro | ☐ | Validar cards por modelo e teste. |
| Contraste correto | ☐ | Conferir heros, subtítulos e botões. |

## Rotas do smoke

- `/Labels/PrintWizard`
- `/Labels/Branding`
- `/Administration/BrandAssets`
- `/Administration/BrandAssets/Create`
- `/Labels/VisualReview`
- `/Labels/Demo`
- `/Labels/History`

## Status geral

**Aguardando validação visual autenticada em ambiente com PostgreSQL e navegador.**
