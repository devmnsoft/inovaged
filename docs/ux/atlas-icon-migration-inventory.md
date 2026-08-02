# Inventário de migração de ícones Atlas

## Critérios

A varredura considera `app-icon`, `atlas-icon`, SVG/`use` direto e Bootstrap Icons em Razor, HTML e JavaScript. Atlas funcional exige nome no registry, `symbol` correspondente, tamanho homologado e rótulo quando informativo.

## Resultado da auditoria

| Categoria | Diagnóstico | Tratamento |
|---|---|---|
| Atlas funcional | `app-icon` é o uso predominante nas superfícies Atlas. | Mantido e estendido com alias `atlas-icon`. |
| Atlas quebrado | O registry da baseline continha inicializadores sem fechamento, impedindo compilação. | Inicializadores corrigidos. |
| Ícone inexistente | Nomes desconhecidos podiam resultar em definição nula. | `missing` em Development e `circle-question` em Production, com warning. |
| Mistura indevida | Referências `bi bi-*` permanecem em telas legadas. | Não foram removidas em massa sem inspeção funcional; são dívida explicitamente legada. |
| Sem label | Ícones decorativos sem label são ocultos da árvore acessível; informativos recebem `role=img`. | Contrato preservado. |
| Tamanho incorreto | A folha não cobria 14, 18 e 40 px. | Escala oficial completa: 14, 16, 18, 20, 24, 32 e 40. |
| Geometria duplicada | Muitos conceitos distintos compartilham o mesmo path documental na baseline. | Registrado como bloqueio de aprovação visual; não mascarado como concluído. |
| Arquivos | Tipo visual poderia ser inferido de formas divergentes. | `IFileVisualResolver` centraliza extensão e MIME para PDF, Office, imagem, texto, CSV, ZIP, DICOM e genérico. |

## Comando reproduzível

```bash
rg -n '<atlas-icon|<app-icon|bi bi-|<i class="bi|<svg|<use href' InovaGed.Web --glob '*.cshtml' --glob '*.html' --glob '*.js'
```

A migração só pode ser declarada completa após inspeção renderizada de cada ocorrência, inclusive estados dinâmicos.
