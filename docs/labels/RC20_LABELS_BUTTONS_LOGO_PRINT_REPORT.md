# RC20 — botões, logo e impressão de etiquetas

## Problemas encontrados
Submits podiam manter texto de carregamento e não havia correlação cliente/servidor em todas as ações.

## Causa raiz
Ausência de identificador por submissão e restauração não padronizada do submitter.

## Arquivos alterados
Models e views de PrintWizard/LocDesk, controller, script de submit e partial compartilhada de logo.

## Correções aplicadas
Cada formulário envia `ClientActionId`; cinco POSTs emitem `LABEL_ACTION_SUBMITTED`; todos usam `formaction` de Labels e fallback de 12 segundos. A logo só é emitida após validação de Data URI `data:image/...;base64,`; base64 nunca é logado.

## Como validar
No navegador autenticado, confira Network, eventos correlacionados, prévia, impressão, histórico e `window.print()`.

## Evidências de build/publish
O gate RC20 inspeciona actions, script e partial. O smoke visual depende de ambiente autenticado com dados.

## Pendências
Executar roteiro manual no IIS com logo cadastrada e impressora homologada.
