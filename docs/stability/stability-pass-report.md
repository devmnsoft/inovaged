# Functional Navigation Stabilization

## Resumo

Esta rodada fecha o contrato automatizado de navegação: inventário versionado, lista única de rotas críticas, execução HTTP com tempo de resposta e relatórios JSON/Markdown. O gate preserva autorização e trata redirects, 401 e 403 como resultados válidos, mas nunca aceita HTTP 500 ou assinaturas conhecidas de exceções no corpo.

## Rotas testadas

As 28 rotas críticas estão em `InovaGed.Environment.Doctor/quality-routes.json`. O resultado específico do ambiente é gravado em `artifacts/quality-gate/route-smoke-report.{md,json}`.

## Rotas corrigidas

- O comando público `route-smoke` passou a existir e compartilhar exatamente o mesmo check executado pelo `quality-gate`.
- Os aliases documentados `schema-check`, `di-check`, `razor-check`, `icon-check`, `admin-links-check` e `layout-check` permitem diagnóstico isolado.
- `/Administration/Permissions`, `/Poc`, `/Protocols/WorkQueue` e `/DocumentQuality` foram adicionadas ao conjunto crítico.

## Rotas desabilitadas por implantação

Nenhuma rota funcional foi artificialmente desabilitada. A view compartilhada `ModuleUnderConstruction` está disponível somente para módulos que tenham dependência técnica real e explicitamente registrada.

## Erros 500 eliminados

O gate rejeita 500 e também respostas aparentemente bem-sucedidas que contenham assinaturas de RuntimeCompilation, `DatabaseSchemaException`, materialização Dapper ou serviço DI não resolvido. A confirmação quantitativa depende da aplicação ativa via `QUALITY_GATE_BASE_URL`.

## Warnings restantes

- Sem `QUALITY_GATE_BASE_URL`, o smoke gera relatórios marcados como incompletos e retorna warning; ele não simula sucesso.
- Checks de schema dependem de uma conexão PostgreSQL disponível, salvo execução diagnóstica com `--no-db-required`.

## Migrations adicionadas

Nenhuma. Esta entrega não altera schema.

## Serviços DI registrados

Nenhum registro adicional foi necessário; o `di-check` existente permanece parte do quality gate.

## Correções Dapper aplicadas

Nenhum mapeamento foi alterado nesta rodada; o `dapper-mapping` existente permanece bloqueante no quality gate.

## Pendências reais

Executar o smoke contra cada ambiente publicado, autenticado e anônimo quando aplicável, arquivar os relatórios e investigar qualquer resultado diferente de 200, 302, 401 ou 403 pela correlação dos logs. Não substituir falhas funcionais pela tela de implantação.
