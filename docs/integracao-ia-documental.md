# Integração de IA documental

Estado após a estabilização sobre o baseline `171ee30b0c85bf2c2906b7d3d7910066cd50aed8`.
Nada deste trabalho foi publicado, integrado ou aplicado em produção.

## Escopo

A IA continua opcional e subordinada ao GED. Busca, OCR, upload, protocolo e custódia não dependem do provedor. Se a política, a credencial ou o provedor falharem, o GED e a pesquisa convencional seguem disponíveis.

As jornadas já entregues foram preservadas e corrigidas: resumo, preenchimento, tipo documental, governança e administração. Protocolo assistido, comparação, voz e multimodalidade ficam para o próximo incremento.

## Jornadas

| Jornada | Tarefa | Catálogo | O que grava |
|---|---|---|---|
| Pergunte ao acervo | `AskCollection` | fontes já autorizadas da busca | não altera documento; trechos convencionais permanecem se a síntese falhar |
| Resumir documento | `Summarize` | OCR da versão | não altera documento |
| Sugerir preenchimento | `ExtractMetadata` | OCR da versão | título, descrição e sigilo confirmados |
| Sugerir tipo documental | `SuggestClassification` | `ged.document_type` ativo | `type_id` e a linha canônica de classificação do tipo |
| Sugerir classificação arquivística | `SuggestArchivalClassification` | itens ativos da versão mais recente de `ged.classification_plan_version` | `classification_id` e `classification_version_id` |

Tipo documental não é classe do plano. A classificação arquivística não inventa código, prazo, evento ou destino. HOLD, empréstimos e impedimentos não são alterados por essas gravações. A ausência de sugestão é válida.

`SupportProtocol` e `CompareDocuments` aparecem na administração com o motivo de indisponibilidade. O salvar rejeita tarefa fora de `AiTaskCatalog.Supported`.

## Sugestão e aplicação

O servidor recupera o resultado da execução. O navegador envia os campos selecionados e as correções do revisor. Ele não substitui o resultado original.

Toda aplicação exige `ExecutionId` da execução do mesmo tenant e do mesmo revisor, da tarefa correta, com resultado válido, vigente, fontes comprováveis e a versão atual do documento. A autorização é reavaliada na hora.

`isConfidentialSet` indica que o revisor selecionou o campo. `isConfidential` é o valor proposto. Campo não selecionado preserva o sigilo atual. Seleção sem valor é rejeitada. Aplicar `true` ou `false` grava esse valor. A mudança de sigilo exige `Security.Manage` (“Gerenciar perfis/sigilo”). `GED.DOCUMENTS` não amplia esse privilégio. A alteração exige justificativa. A auditoria guarda valor anterior, novo, justificativa e revisor.

A IA não sugere redução de sigilo porque o texto não contém dado sensível. Só uma evidência literal de sigilo (`true`) vira sugestão. A proteção atual permanece até decisão humana autorizada.

Um campo sem evidência na versão processada não derruba os demais. A resposta informa cobertura parcial. Indicador numérico declarado pelo modelo não é confiança calibrada e não autoriza aplicar.

Resumo e preenchimento recusam texto acima de 120.000 caracteres sem enviar e sem truncar. Tipo documental e classificação arquivística podem truncar o texto e limitar o catálogo a 40 candidatos, sempre com a cobertura declarada.

## Concorrência, auditoria e temporalidade

O token de concorrência é `xmin` do documento, devolvido na sugestão e exigido na primeira gravação. A alteração, o registro em `ged.ai_suggestion_application`, a linha em `ged.app_audit_log` e, quando a classificação muda, a pendência em `ged.ai_retention_recalc_pending` ocorrem na mesma transação. O commit deixa o recálculo concluído ou a pendência recuperável. Metadados não enfileiram temporalidade.

A identidade da revisão é `operation_key`, SHA-256 de tenant, execução, documento, versão, revisor e tarefa. O fingerprint cobre só a decisão canônica do revisor. Repetir a mesma operação devolve o resultado persistido e o estado real da pendência vinculada, mesmo se o resultado da IA expirou ou a versão do documento avançou. A mesma identidade com outra decisão responde 409. Uma revisão intencional nova é outra execução. Registros antigos recebem a chave na migration; linhas legadas duplicadas ficam com sufixo e o replay encontra a mais antiga. A equivalência também lê o `decision_json` anterior.

A classe e a versão do plano são gravadas juntas. A classe precisa existir na versão vigente do plano, no caminho assistido e em `DocumentCommands.ApplyClassificationAsync`. A versão documental usada é `current_version_id`. Zero linhas nesse predicado é conflito, sem escolher outra versão por conveniência.

Tags e metadados que não fazem parte da alteração permanecem. A classificação rápida do GED chama `SaveManualAsync` com tags e metadados nulos, e o serviço não apaga o que não foi enviado.

O processamento da pendência usa `RetentionRecalcService.RunOneAsync`. A reivindicação é `FOR UPDATE SKIP LOCKED`, com token e arrendamento de 90 segundos. Tentativa e erro ficam em `attempts` e `last_error`, sem texto documental. A pendência só recebe `resolved_at` depois do sucesso. Falha ou cancelamento libera o token e mantém a linha. Outro consumidor vê “em processamento” enquanto o arrendamento vale. Reaplicar uma revisão concluída não cria outra pendência. HOLD, empréstimo e impedimento não são alterados por esse recálculo.

Não há varredura em segundo plano. A recuperação operacional é a tentativa imediata depois do commit e `POST HospitalDocuments/RetryRetention`, que exige edição do documento, audita `AI_RETENTION_RETRY` e informa resolvida, em processamento ou pendente. Consulta: `GET HospitalDocuments/RetentionPending`.

## Fontes, retenção e consumo

Fontes válidas são pares documento+versão em texto. Número, objeto, array, booleano, GUID vazio ou inválido, campo obrigatório ausente e pares inconsistentes são fonte corrompida. O codec não lança e não libera o resultado. Formato legado `{documento}/{versão}/texto-extraido` só é migrado depois que cada versão existe no tenant e o documento coincide. Resultado sem fonte comprovável fica inacessível. `result_expires_at` vale na primeira leitura e na primeira aplicação. O replay de uma revisão já gravada continua disponível depois da expiração e mostra a decisão humana, sem o texto do modelo. Resultado malformado, inclusive `Failure` textual, não produz HTTP 500 genérico.

`decision_json` e o detalhe de auditoria podem conter os valores que o revisor aplicou. Eles permanecem na linha porque são o rastro mínimo da decisão humana. Não entram em log. O JSON novo não guarda OCR nem evidência do modelo. O histórico só devolve esses campos a quem pode ver o documento agora. Conhecer `executionId` ou `applicationId` não amplia o acesso.

Quando uma estimativa já liquidada recebe uso real de uma execução enviada, a diferença entre o reportado e o liquidado ajusta o período original uma única vez (`reconciled_delta`, `usage_reconciled_at`). O valor liquidado original permanece. Reserva nunca enviada continua em zero. Conclusão válida não é substituída por falha posterior nem tem a retenção renovada por repetição.

## Telas

O parcial `_DocumentAiAssist` está no visualizador hospitalar, no painel lateral do GED e no detalhe do documento. Ele também lista o histórico de revisões e as pendências de temporalidade do documento, com paginação estável. As situações são: sugestão gerada, revisão registrada sem alteração, aplicação concluída, aplicação com recálculo pendente, pendência recuperada e resultado da IA expirado. Não há tela administrativa nova para esse histórico.

A administração nomeia as tarefas e explica as indisponíveis. Os scripts `document-summary.js` e `document-assist.js` usam delegação no documento, cancelam a espera, ignoram resposta fora de ordem e bloqueiam o segundo clique enquanto a gravação está pendente. Conflito e conclusão parcial aparecem com o texto do servidor. `retentionPending` ou `partial` mostra “Conclusão parcial”, inclusive quando a repetição devolve `alreadyApplied` e a pendência ainda existe. O texto exibido passa por escape.

Como usar: gere a sugestão, compare atual, sugerido e corrigido, confira a fonte pelo nome e pela versão e confirme só os campos escolhidos. Cancelar descarta a resposta atrasada. Tab percorre os controles. A nova tentativa de temporalidade fica na seção de pendências do próprio documento.

## Banco

`database/migrations/2026_10_05_document_ai_application_integrity.sql` adiciona `usage_reconciled_at` e `reconciled_delta`, e cria `ged.ai_suggestion_application` e `ged.ai_retention_recalc_pending`.

`database/migrations/2026_10_06_document_ai_review_recovery.sql` adiciona `operation_key`, o índice único `ux_ai_suggestion_application_operation` e as colunas `attempts`, `last_error`, `claimed_at` e `claim_token`. O backfill usa a mesma fórmula SHA-256 do código. A reexecução é idempotente. As duas migrations estão no manifesto, em `required_migrations.json` e uma vez em `apply_all_required_migrations.sql`. Migrations já aplicadas e com checksum não foram reescritas.

O script consolidado teve ajustes só em arquivos fora do manifesto de checksum, mais a ordem dos `\ir`: coluna `created_by` antes do índice de upload; índices de `ged.loan_request` condicionados à existência da tabela; modos e fila de etiqueta depois de `ged.label_print_history`; tabelas mínimas `ged.app_role` e `ged.user_role` junto do `app_user` já criado pelo consolidado; índices de `document_signature` condicionados à tabela; `2026_07_signature_cms_end_to_end.sql` antes do runtime que indexa a assinatura. `ged.loan_request` continua definida em `gedscript.sql` e não é criada pelas migrations.

## Homologação

Use banco e aplicativo locais, documentos fictícios e as variáveis de ambiente. Não use dump, paciente, credencial de produção ou segredo no repositório.

1. Aplique as migrations de IA em PostgreSQL descartável. `2026_10_05` e `2026_10_06` são idempotentes.
2. Defina `INOVAGED_AI_PG_DSN` e rode `PostgresAiGovernanceStoreBehaviorTests` e `DocumentAiCyclePostgresTests`. Sem a variável, o fato é ignorado na descoberta: isso é não executado, não aprovação. Rode as classes em separado: a de governança trunca as tabelas de IA.
3. Para HTTP, defina `INOVAGED_AI_HTTP_BASE`, os perfis fictícios e os GUIDs do documento e da versão. Rode `scripts/homologation/document-ai-http.ps1`. Login com destino em `/Account/Login` não conta como autenticação. Sem a base, o script termina com código 2 e `NAO EXECUTADO`. Código 0 só ocorre quando a matriz do script passa. O provedor local é `Deterministic`, e só com `INOVAGED_AI_DETERMINISTIC=1` no processo da aplicação. Ele não chama HTTP e não homologa Groq, Gemini ou DeepSeek. A política do tenant precisa usar o mesmo provedor.
4. Repita com `GROQ_API_KEY`, `GEMINI_API_KEY` ou `DEEPSEEK_API_KEY` quando a credencial for válida.
5. Confira no navegador, em desktop e em largura estreita: resumo, preenchimento, tipo documental, classificação arquivística, histórico, pendência, cancelamento, duplo clique, conflito e conclusão parcial.

Recuperação: liste `ged.ai_retention_recalc_pending` com `resolved_at` nulo. Quem edita o documento aciona `RetryRetention`. Se `claimed_at` tiver menos de 90 segundos, a resposta é “em processamento”. Depois desse prazo, outra tentativa reivindica a mesma linha. Sucesso preenche `resolved_at` e zera o parcial da aplicação. O erro gravado é um código curto, por exemplo `temporalidade:falha`.

Matriz mínima: sigilo não selecionado preservado; valor escolhido aplicado; redução sem `Security.Manage` negada; execução de outro documento ou tarefa negada; resultado expirado não reutilizado; repetição sem efeito duplicado; edição concorrente em conflito; tags e metadados não selecionados preservados; falha de auditoria revertida com a gravação; recálculo falho com conclusão parcial; legado sem fonte inacessível; consumo tardio reconciliado uma vez; tipo documental separado da classe do plano; campo sem evidência sem sugestão inventada; Pergunte ao acervo configurável; IA indisponível com GED e busca convencionais; outro tenant sem conteúdo nem aplicação.

## Verificação desta sessão

Baseline `c9afa95`. Nenhum commit posterior. Produção não foi alterada.

| Verificação | Resultado |
|---|---|
| `DocumentAiIntegrityTests`, inclusive tipos JSON inválidos e identidade estável da revisão | passou, 10/10 |
| Provedor `Deterministic` com e sem `INOVAGED_AI_DETERMINISTIC` | passou, 2/2; nenhuma chamada HTTP |
| `DocumentAiCyclePostgresTests` em `ai_cycle` (`127.0.0.1:55432`) | passou, 3/3: pendência na mesma transação, falha sanitizada, retomada, HOLD e empréstimo preservados, consumidores concorrentes, versão canônica do documento, conflito quando a classe sai do plano vigente |
| `PostgresAiGovernanceStoreBehaviorTests` no mesmo banco, depois de existir `ged.tenant.code` | passou, 16/16 |
| `MigrationManifestTests` | passou, 1/1 |
| Suíte `InovaGed.Application.Tests` sem `INOVAGED_AI_PG_DSN` | 529 aprovados, 36 falhas, 20 ignorados |
| Comparação com a baseline 525/36/17 | os 4 aprovados a mais são os testes novos de identidade, codec e provedor determinístico; os 3 ignorados a mais são `DocumentAiCyclePostgresTests` sem DSN; as 36 falhas continuam nos contratos já existentes de etiquetas, shell, identidade, DI, Web API e segredo de configuração |
| Migrations `2026_10_02` a `2026_10_06` e reexecução da `2026_10_06` em `ai_cycle` | passou |
| `apply_all_required_migrations.sql` em banco vazio `ai_install` | não homologada. Os bloqueios de `created_by` e dos índices de `ged.loan_request` foram ultrapassados. A execução atual para em `database/migrations/2026_07_signature_cms_agent_runtime.sql:153` com `column "signature_id" does not exist`, porque a tabela criada antes não tem a coluna esperada pelo índice. As migrations de IA do final do script não foram alcançadas por esse caminho |
| `scripts/homologation/document-ai-http.ps1` sem `INOVAGED_AI_HTTP_BASE` | não executado, código 2. O falso positivo do perfil sem acesso foi removido. O ciclo HTTP com aplicativo local não foi percorrido |
| Groq, Gemini e DeepSeek | não executado; credenciais ausentes |
| GED e visualizador no navegador, teclado, largura estreita, reabertura do painel e resposta atrasada | não executado; não há ferramenta de navegador nesta sessão e o aplicativo não foi iniciado |

## Pendências

- Concluir a instalação limpa do script consolidado a partir de `2026_07_signature_cms_agent_runtime.sql:153`, sem reescrever migration com checksum.
- Criar `ged.loan_request` no fluxo oficial. Hoje ela permanece só em `gedscript.sql`.
- Homologar o ciclo HTTP com aplicativo local, dados fictícios e provedor `Deterministic`.
- Homologar Groq, Gemini ou DeepSeek com credencial válida.
- Conferir as telas no navegador.
- Protocolo assistido, comparação, voz e multimodalidade, depois deste ciclo.
