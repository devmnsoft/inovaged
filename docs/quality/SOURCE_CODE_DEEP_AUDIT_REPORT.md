# Source Code Deep Audit + Operational Feature Evolution

## Resumo da auditoria

Foi executado inventário transversal dos projetos, busca estática por materialização Dapper, conversões, padrões Razor frágeis, rotas, DI e schema SQL. O runtime .NET não está instalado no contêiner, portanto compilação, testes e Doctor ficaram bloqueados pelo ambiente e não são reportados como aprovados.

## Erros encontrados e bugs corrigidos

- A consistência de upload materializava diretamente o modelo público contendo `DateTimeOffset?`. Foi criado `DbRow` interno mutável e conversão explícita de `DateTime` UTC.
- `/Dashboard`, `/GlobalSearch` e `/Administration/Consistency` não estavam no contrato operacional de rotas. As rotas foram implementadas/inventariadas.
- O dashboard construía cards em `foreach` Razor inline. O catálogo agora é tipado e preparado no bloco Razor.
- O dashboard não integrava pendências nem ações globais. Foram adicionados links navegáveis; métricas não disponíveis no agregado são mostradas como “Abrir”, nunca como zero inventado.
- A auditoria de inconsistências faz descoberta de tabela/coluna antes de consultar objetos opcionais e nunca consulta documentos sem tenant globalmente, preservando isolamento.

## DI, Dapper, Razor e conversões

`IConsistencyAuditService` tem implementação scoped no composition root. O caminho Dapper corrigido usa SQL → `UploadBatchConsistencyDbRow` → modelo público e controla `DateTimeKind`. A busca usa o serviço multi-provider existente, que aplica `CurrentContext.TenantId`, filtra URLs relativas e degrada falhas isoladas por provider. A nova Razor usa botões com `type` explícito, modelos tipados e CSS externo.

## Migrations e regras de negócio

Os 130 arquivos de migrations, manifesto e agregador foram inventariados por busca estática; nenhuma alteração de schema foi necessária. Não foram encontrados `DROP`, `TRUNCATE` ou seed destrutivo introduzidos nesta rodada. As funcionalidades novas são somente leitura, tenant-aware e não corrigem inconsistências sem confirmação. O relatório de consistência informa indisponibilidade de schema sem transformar ausência opcional em erro 500.

## Rotas e riscos restantes

O manifesto inclui respostas autenticadas/redirects esperadas. A validação HTTP real requer aplicação, PostgreSQL e SDK .NET, indisponíveis nesta execução. A busca operacional atualmente agrega providers registrados para navegação, documentos, protocolos, empréstimos e usuários; caixas, etiquetas, tarefas, incidentes, publicações e medições ainda requerem providers dedicados. A varredura encontrou outros DTOs materializados diretamente por Dapper e Razor legado inline fora dos fluxos alterados; devem ser migrados incrementalmente, com testes de compatibilidade, em vez de substituição indiscriminada.
