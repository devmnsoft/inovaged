# GED Workspace 2.0 — Design Premium e Operação Documental

## Objetivo

Transformar `/Ged` em uma superfície operacional integrada sem substituir os fluxos reais existentes de upload, OCR, classificação, preview ou gestão de pastas.

## Telas alteradas

- `/Ged`: hero executivo, atalhos, filtros, upload em contexto, seleção e ações documentais.
- `/Administration`: validada como referência Atlas existente, com hero, status, KPIs e cards agrupados.

## Componentes criados

O workspace usa hero compacto, painel de filtros, árvore existente, lista Smart/tabela, central de upload, barra de seleção e drawer lateral responsivo.

## Ações por documento

Preview, detalhes, classificação, OCR, download e movimentação foram preservados. Foram adicionados acessos contextuais para gerar etiqueta, consultar inteligência e criar tarefa.

## Preview lateral

O preview possui estados vazio, carregando e erro, fechamento explícito, fechamento por `Esc`, rolagem interna e drawer de tela inteira no celular. O conteúdo real continua sendo carregado pelo endpoint `DocumentPanel`.

## Filtros

Texto, OCR, classificação, período e dado sensível filtram os documentos carregados. As limitações e próximas integrações estão em `GED_WORKSPACE_2_FILTERS.md`.

## Seleção em lote

Checkboxes, contador e barra contextual permanecem integrados às ações reais. Ações ainda sem endpoint aparecem desabilitadas com o microtexto “Ação em implantação”, sem rotas fictícias.

## Integração com etiquetas

“Gerar etiqueta” abre o assistente com tipo `DOCUMENT`, documento selecionado, modo `FACTORY` e template padrão, sem solicitar IDs ao usuário.

## Integração com inteligência

Cada documento oferece acesso ao Smart GED sem aplicar automaticamente sugestões de classificação ou temporalidade.

## Integração com workflow

Cada documento oferece acesso ao Smart Workflow com seu contexto. A configuração e as permissões continuam sob responsabilidade do módulo.

## CSS e JavaScript

`css/pages/ged-workspace.css` define a camada visual responsiva com tokens Atlas. `js/ged-workspace.js` gerencia filtros e proteção contra ações repetidas; `js/ged-preview.js` gerencia o fechamento acessível.

## Como validar

1. Acesse `/Ged` autenticado e selecione uma pasta.
2. Use filtros, selecione documentos e alterne Smart/Tabela.
3. Abra o preview, feche pelo botão e pela tecla `Esc`.
4. Abra “Gerar etiqueta” e confirme o documento pré-selecionado.
5. Teste upload por seleção e arrastar/soltar.
6. Execute `scripts/smoke-routes-local.ps1` contra uma instância configurada.

## Pendências futuras

Levar todos os filtros ao backend paginado, integrar histórico de etiquetas ao read model e disponibilizar endpoints transacionais para as novas ações em lote.
