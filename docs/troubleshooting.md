# Troubleshooting do InovaGED

## Upload não aparece na pasta

Confira batch, item, pasta destino, versão documental, permissões de storage e logs do upload.

## OCR com erro

Verifique fila, arquivo original, worker, dependência externa, timeout e mensagem registrada.

## Preview não abre

Valide geração, caminho físico, MIME type, versão documental e autorização do usuário.

## Busca não encontra

Confira filtros, tenant, OCR concluído, índices e dados cadastrais do documento.

## Menu não aparece

Revise perfil, permissões, claims, layout e proteção no controller.

## IIS 500.19

Revise `web.config`, runtime instalado, módulos ASP.NET Core, permissões e herança de configuração.

## PostgreSQL column not found

Aplique migrations pendentes, confirme schema `ged` e revise scripts executados no ambiente.

## Pool de conexão

Monitore conexões abertas, consultas longas, timeouts e paralelismo de workers.
