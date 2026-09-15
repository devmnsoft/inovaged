# Label Studio RC45 — homologação operacional

## Autoridades preservadas

A RC45 mantém `LabelCanvasRenderService` como autoridade única de validação e renderização. Designer,
prévia, Test Lab e impressão continuam consumindo o mesmo contrato Canvas em milímetros. O catálogo e
os modelos de fábrica não foram alterados; em particular, nenhum modelo LocDesk foi incluído no catálogo
de fábrica.

## Gate de publicação

O checklist classifica achados como `BLOCKER`, `WARNING` ou `INFORMATION`, agrupa-os por conteúdo,
layout, identidade, impressão e segurança e calcula prontidão determinística. A publicação exige:

1. Canvas estruturalmente válido, com bindings, assets, condições e dimensões seguros;
2. evidência de ao menos uma amostra apta no Test Lab para o hash exato do rascunho;
3. mídia compatível, quando informada;
4. `lock_version` atual, validado novamente em atualização atômica;
5. confirmação e justificativa para alertas.

A evidência do laboratório é registrada como evento do modelo, sem criar job, histórico de impressão,
trace operacional ou mutar o registro analisado. A versão publicada guarda snapshot e hash imutáveis, e
o evento de publicação inclui checklist, prontidão e justificativa.

## Preflight em lote

`CheckBatchDetailedAsync` mantém a ordem da seleção, impõe limites de seleção e concorrência, propaga
cancelamento, informa progresso e retorna estado por registro e resumo temporal. O fluxo é somente leitura
e reutiliza `CheckAsync`; portanto, não cria jobs nem traces e não atualiza os objetos avaliados.

## Homologação

1. Abrir um rascunho próprio e executar o Test Lab com registros autorizados.
2. Confirmar que um caso bloqueado não habilita a publicação.
3. Corrigir o Canvas, repetir o laboratório e abrir o diálogo de publicação.
4. Informar a justificativa, publicar e conferir versão, hash e evento de auditoria.
5. Tentar reenviar a mesma requisição e confirmar conflito de concorrência.
6. Executar `labels-operational-homologation-rc45` no Environment Doctor.

Não houve mudança de banco nesta RC; as tabelas e índices aditivos das RCs anteriores são reutilizados.
