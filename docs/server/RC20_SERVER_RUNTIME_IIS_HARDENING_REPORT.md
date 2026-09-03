# RC20 — runtime do servidor e hardening IIS

## Problemas encontrados
A publicação podia omitir `System.Diagnostics.DiagnosticSource` exigido em runtime; sondagens externas atingiam caminhos sensíveis. Isso é falha de dependência/publicação, não migration.

## Causa raiz
Dependência apenas transitiva em publish anterior e tráfego automatizado da Internet.

## Arquivos alterados
`Directory.Packages.props`, projeto Web, `SchemaHealthService`, middleware, `web.config`, robots/favicon e quality gate.

## Correções aplicadas
A referência 9.0.0 é direta no executável; SchemaHealth possui `RuntimeDependencyError`, sem script sugerido; HTTPS/HSTS e bloqueios retornam resposta opaca e registram `SUSPICIOUS_REQUEST_BLOCKED`.

## Como validar
Execute o gate RC20, build Release e publish limpo. Confirme DiagnosticSource, Npgsql e Web DLL no diretório publicado; teste `/.env`, `/.git/config` e `/robots.txt`.

## Evidências de build/publish
O gate verifica os contratos no repositório. A publicação Windows e reinicialização IIS devem ser realizadas no servidor conforme runbook do pedido.

## Pendências
Validar conexão real, logs e troca atômica preservando configuração e storage persistente no IIS de produção.
