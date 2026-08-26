# Labels Professional Print Closing

## Fluxo de impressão

A Central de Etiquetas conduz o operador pelo assistente: origem, modelo compatível, prévia, impressão e registro imutável no histórico. O fluxo LocDesk preserva formulário e prévia separados; o lote usa seleção por checkbox e consolida itens em trabalho de impressão.

## Modelos disponíveis

O catálogo mínimo é `FACTORY_BOX_V1`, `FACTORY_DOCUMENT_V1`, `LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_V1` e `LOCDESK_PASTA_HOL_V1`. O catálogo nunca substitui um modelo por outro.

### LocDesk padrão

O modelo padrão mantém sua identidade e o cabeçalho **ARQUIVO LOCDESCK ANANINDEUA**.

### LocDesk HOL

O HOL permanece um template independente, selecionável por seu código, com campos hospitalares, dimensões físicas e QR legível.

## Calibração

`/Labels/Calibration` salva papel, impressora, quatro margens, escala, deslocamentos horizontal/vertical e preferência padrão por tenant e usuário. A folha de teste A4 contém régua em mm, eixos centrais e referências de 20 mm. No diálogo do navegador, use escala 100% e desative cabeçalhos/rodapés.

## Impressão em lote

`/Labels/Batch` lista origens reais. Modelo e origem são validados no servidor, IDs repetidos são consolidados e cada item permanece rastreável. Falhas de validação não devem ser registradas como incidentes técnicos.

## HTML imprimível

A saída primária é HTML com `_PrintLayout.cshtml` e `labels-print.css`, sem navegação ou decoração de tela. A fila também oferece PDF quando o renderizador configurado estiver disponível, sem impor nova dependência pesada.

## Histórico e reimpressão

`/Labels/History` oferece filtros e KPIs. `/Labels/History/{id}` mostra usuário, origem, cliente, template, hash e snapshot. `POST /Labels/History/{id}/Reprint` exige justificativa e cria um novo registro, preservando o original.

## Auditoria e incidentes

Prévia, impressão, lote, reimpressão e calibração devem produzir eventos `LABEL_*`. Erros técnicos são classificados como template ausente, falha de render, histórico, lote ou calibração; erros corrigíveis pelo usuário permanecem apenas como validação amigável.

## Migrations

A migration idempotente `2026_08_26_label_print_calibration.sql` evolui a tabela legada sem apagar configurações. Ela consta no catálogo e no aplicador consolidado. O SchemaHealth inclui templates, impressão, histórico, calibração e rascunhos LocDesk.

## Como validar

1. Aplique `database/apply_all_required_migrations.sql` com `psql`.
2. Execute `dotnet restore InovaGed.sln` e `dotnet build InovaGed.sln -v:minimal`.
3. Percorra Central, Assistente, Lote, Histórico, Detalhes, Calibração e LocDesk.
4. Confira LocDesk padrão e HOL, primeira impressão e reimpressão com/sem justificativa.
5. Imprima a folha de teste e uma folha A4 em escala 100%.

## Pendências futuras

A geração de PDF depende do renderizador já configurado no ambiente. Perfis específicos de drivers e telemetria de impressoras físicas podem ser evoluídos sem alterar o snapshot auditável.
