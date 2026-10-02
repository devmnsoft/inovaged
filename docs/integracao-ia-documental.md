# Integração de IA documental

## Escopo e segurança

A IA é opcional e subordinada ao GED. O SmartSearch recupera fontes já autorizadas; somente trechos/versões selecionados são enviados. Conteúdo documental é dado não confiável, nunca instrução. Se política, credencial ou provedor falhar, GED, OCR e pesquisa convencional continuam disponíveis.

O gateway não executa protocolo, eliminação, HOLD, temporalidade ou empréstimo. Resumo e sugestões são revisáveis e não alteram o original.

## Configuração segura e política efetiva

A configuração global `DocumentAi` começa com `Enabled=false`, autoriza provedores/modelos e estabelece máximos. Chaves existem somente nas variáveis `GROQ_API_KEY`, `GEMINI_API_KEY` e `DEEPSEEK_API_KEY`; a UI mostra apenas “configurada/ausente”. Nunca grave segredos em banco, logs ou frontend.

A migration `2026_10_02_document_ai_governance.sql` cria a política explícita por tenant. Ausência de registro bloqueia antes da rede. **Administração → IA por cliente** grava habilitação, tarefas, provedor/modelo e limites menores. Uma alteração incrementa a revisão. O gateway exige coincidência exata entre política efetiva e allowlist global, sem fallback. Tenant e usuário vêm de `ICurrentUser`, não do navegador.

`BaseUrl` exige HTTPS e host oficial (`api.groq.com`, `generativelanguage.googleapis.com` ou `api.deepseek.com`) ou host explicitamente confiável.

## Contratos dos provedores

Referências oficiais usadas no desenho (a consulta automatizada recebeu HTTP 401 em 2 de outubro de 2026; repita em homologação):

- [Groq API](https://console.groq.com/docs/api-reference) e [Structured Outputs](https://console.groq.com/docs/structured-outputs).
- [Gemini text generation](https://ai.google.dev/gemini-api/docs/text-generation) e [Structured output](https://ai.google.dev/gemini-api/docs/structured-output).
- [DeepSeek chat completion](https://api-docs.deepseek.com/api/create-chat-completion) e [JSON output](https://api-docs.deepseek.com/guides/json_mode).

O modelo precisa constar em `AllowedModels` e `StructuredOutputModels`; capacidade genérica do provedor não basta. O validador local aceita somente `type`, `required`, `enum`, `properties`, `additionalProperties`, `items`, `minLength`, `maxLength`, `minItems`, `maxItems`, `minimum` e `maximum`. Palavra-chave desconhecida é rejeitada, não ignorada. Tipos incorretos, JSON malformado, saída vazia/truncada e estrutura incompatível viram `InvalidOutput`.

## Execuções, cota e recuperação

`ai_execution` registra tenant/usuário, tarefa, provedor/modelo, referências de documento/versão, correlação, revisão, idempotência, estado, duração e uso — nunca conteúdo integral ou segredo. A unicidade `(tenant_id, idempotency_key)` evita execução local duplicada.

A reserva usa transação e atualização condicional em `ai_monthly_usage`, segura entre instâncias. Limite de entrada é ocorrência distinta da cota. O acerto libera reserva e contabiliza uso reportado. Timeout após envio vira `RemoteOutcomeUnknown` e não é reenviado automaticamente. `ged.expire_ai_reservations()` libera reservas abandonadas; execução que chegou a `Running` expira como incerta. Não se promete cobrança única pelo provedor.

## Resposta fundamentada e resumo

`DocumentEvidenceService` combina cobertura de recuperação e síntese, interpreta `answered`, `partial_coverage` e `insufficient_evidence`, rejeita combinações contraditórias, limita afirmações/texto/referências, descarta referência desconhecida e informa redução. Fontes convencionais permanecem visíveis. Após a chamada, acesso e versão são revalidados. Uma referência é evidência para revisão humana, não prova semântica automática.

No visualizador hospitalar, **Resumir documento** processa a versão aberta e apresenta assunto, fatos explícitos, datas, pendências, limitações de OCR, correlação e acesso ao texto original. Sem OCR há erro compreensível. Acima de 120.000 caracteres o servidor recusa sem truncar; processamento em partes permanece pendente. Cancelamento é visível e o original não é alterado.

## Homologação

1. Aplique a migration em PostgreSQL descartável; valide instalação limpa e upgrade.
2. Crie política para tenant descartável e confirme: sem política/desabilitado = zero chamadas; outro tenant = 404/403.
3. Dispare o mesmo idempotency key simultaneamente e confirme uma execução; concorra reservas próximas da cota.
4. Simule 429, credencial inválida, JSON incorreto, timeout e cancelamento; confira estado, correlação e reserva.
5. Revogue acesso ou troque versão durante uma chamada e confirme descarte.
6. Use documentos fictícios para cada provedor/modelo homologado. Mocks não homologam integração real.
7. Verifique teclado, foco, loading, cancelado, parcial, erro, responsividade e original intacto.
8. Execute regressão de upload, protocolo, custódia e temporalidade.

## Estado verificável desta entrega

- **Implementado:** política persistente fail-closed por tenant; painel do administrador do cliente; execução/idempotência; cota concorrente; estados incerto/cancelado/rejeitado; resumo revisável; validação rigorosa de schema; cobertura e revalidação de fonte/versão.
- **Verificado estaticamente:** nenhuma chave/conteúdo integral persistido; falha de IA não remove fontes convencionais; resumo não grava metadados.
- **Bloqueado:** `dotnet` não existe no ambiente, portanto restore/build/testes .NET não foram executados. Não há PostgreSQL descartável nem credenciais; integração real, autorização HTTP, concorrência e screenshot autenticado não foram executados. Consulta oficial online retornou HTTP 401.
- **Pendente específico:** preenchimento/classificação assistidos e aplicação parcial pelo serviço canônico com token de concorrência; worker para expiração; resumo em partes/fila; teste de conexão global; painel global multi-tenant. Protocolo, comparação, imagens e embeddings permanecem no backlog.

## Evidências desta execução

- Baseline: branch `work`, commit `0edc08999a5a3c4e81eea3e070a29bf27329938f`, árvore limpa.
- `dotnet --info`: bloqueado (`dotnet: command not found`).
- Consulta às documentações oficiais: bloqueada (`HTTP 401 Unauthorized`).
- Nenhuma chamada real foi tentada: não havia credenciais fornecidas.
