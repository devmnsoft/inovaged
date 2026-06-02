# Roteiro de teste — Upload em lote GED

## Pré-requisitos

- Aplicar `database/migrations/20260601_upload_batch.sql` no PostgreSQL.
- Confirmar `DocumentUpload` em `InovaGed.Web/appsettings.json`:
  - `MaxConcurrentUploadsGlobal = 8`
  - `MaxConcurrentUploadsPerUser = 2`
  - `MaxConcurrentUploadsPerBatch = 2`
  - `MaxFileSizeMb = 100`
  - `MaxBatchFiles = 500`
- Confirmar IIS/Kestrel alinhados com `InovaGed.Web/web.config` e `FormOptions.MultipartBodyLengthLimit`.

## Cenário A — dois computadores, 50 + 50 simultâneos

1. Computador 1 abre a mesma pasta GED e seleciona 50 arquivos.
2. Computador 2 abre a mesma pasta GED e seleciona outros 50 arquivos.
3. Iniciar os dois lotes quase simultaneamente.
4. Validar:
   - O navegador envia no máximo 2 arquivos simultâneos por lote.
   - O backend pode retornar 429 recuperável quando os limites globais/usuário/lote forem atingidos.
   - O monitor `/Ged/UploadMonitor` mostra os lotes como `PROCESSING` e depois `COMPLETED` ou `PARTIAL_ERROR`.
   - Arquivos concluídos aparecem na pasta e têm item `COMPLETED` em `ged.upload_batch_item`.
   - OCR/preview ficam em fila (`PENDING`) e não bloqueiam o request de upload.

## Cenário B — 50 arquivos e depois mais 50

1. Enviar 50 arquivos e aguardar conclusão do lote.
2. Abrir novo lote e enviar mais 50 arquivos.
3. Validar:
   - O segundo lote recebe novo `batchId`.
   - A UI não reutiliza arquivos invisíveis nem estado do lote anterior.
   - Ambos os lotes aparecem separadamente no monitor.

## Cenário C — conexão interrompida

1. Iniciar upload com arquivos grandes.
2. Interromper rede/navegador durante o upload.
3. Validar:
   - Itens já persistidos permanecem `COMPLETED`.
   - Itens interrompidos permanecem `RECEIVING` até a rotina `MarkStaleReceivingItemsAsError` marcá-los como `ERROR` recuperável.
   - O usuário consegue reenviar apenas falhos sem duplicar os concluídos.

## Cenário D — 200 arquivos em lotes

1. Enviar 200 arquivos em um ou mais lotes.
2. Validar estabilidade de UI, conexões, memória e fila OCR/preview.
3. Confirmar que o request de upload apenas salva, cria documento/versão, registra o item e enfileira processamento.

## Evidência esperada para win32-status=64

- Se o cliente abortar antes da conclusão, o item fica `ERROR` com `error_step = ClientAbort` ou `StaleReceiving`.
- Se o cliente abortar depois da persistência, o banco confirma `COMPLETED`, e a UI pode reconciliar via `/Ged/UploadBatch/Status/{batchId}`.
- HTTP 200 em log IIS não deve ser considerado sucesso sem confirmação no banco.
