# Configuração do InovaGED

## appsettings

Configure `ConnectionStrings`, `Storage`, `DocumentUpload`, `Ocr`, `Preview`, `Security` e parâmetros de workers conforme o ambiente.

## Banco

- Banco PostgreSQL com schema `ged`.
- Migrations aplicadas em ordem.
- Índices de busca, OCR, dashboard e upload habilitados.

## Storage

- Diretórios para originais, previews, temporários e chunks.
- Permissões de leitura/escrita para o usuário do processo.
- Monitoramento de espaço livre e retenção.

## OCR e preview

- Worker ativo.
- Dependências externas configuradas.
- Timeouts e tamanho máximo alinhados ao upload.
- Logs habilitados para falhas e reprocessamentos.

## Segurança

- Perfis cadastrados.
- Permissões validadas no menu e no controller.
- Usuários reais vinculados ao tenant correto.
