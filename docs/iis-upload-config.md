# Configuração IIS para upload grande

## Pontos de validação

- `maxAllowedContentLength` no IIS.
- `requestTimeout` e limites de proxy/reverse proxy.
- `FormOptions.MultipartBodyLengthLimit` na aplicação.
- Limites `DocumentUpload` no `appsettings`.
- Permissões no diretório de storage e temporários.

## Validação operacional

1. Enviar arquivo dentro do limite configurado.
2. Enviar lote com múltiplos documentos.
3. Enviar arquivo grande por chunk quando habilitado.
4. Confirmar batch, item, documento, versão e fila OCR/preview.
5. Conferir logs em caso de interrupção de conexão.
