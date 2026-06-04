# OCR e Preview

## OCR

OCR é processado por jobs persistidos. Estados esperados: pendente, em processamento, concluído e erro. O sistema deve exibir indisponibilidade quando não houver texto reconhecido para o documento ou filtros selecionados.

## Preview

Preview usa versões documentais existentes e arquivos gerados em storage. A abertura lateral, expansão e nova aba dependem de tipo suportado e permissão.

## Falhas

- Registrar erro com documento, versão, worker e correlationId quando aplicável.
- Não inventar conteúdo, quantidade ou indicador.
- Permitir reprocessamento apenas por ação autorizada ou rotina operacional.
