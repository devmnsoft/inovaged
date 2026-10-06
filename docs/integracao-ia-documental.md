## Evidência operacional atual

Consulte a [auditoria operacional](ai-operational-acceptance.md) para distinguir implementação, evidência real e pendências. A recuperação atual usa bloqueio e transação PostgreSQL no worker existente, com backoff persistido; lease expirável isolado não garante exclusão durante recálculos longos. O upgrade usa um preflight novo antes da migration publicada para preservar identidades com 64 caracteres, sem sufixos maiores que a coluna.

# Integração de IA documental

Estado da implementação sobre o baseline `8f784ba5e64f3fe9345c98beecb368de84db77b7`; evidências e limitações atuais no relatório operacional acima.
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

A identidade da revisão é `operation_key`, SHA-256 de tenant, execução, documento, versão, revisor e tarefa. O fingerprint cobre só a decisão canônica do revisor. Repetir a mesma operação devolve o resultado persistido e o estado real da pendência vinculada, mesmo se o resultado da IA expirou ou a versão do documento avançou. A mesma identidade com outra decisão responde 409. Uma revisão intencional nova é outra execução. O preflight novo atribui a chave canônica à revisão legada mais antiga e um SHA-256 distinto de 64 caracteres às demais; o replay encontra a mais antiga sem exceder a coluna. A equivalência também lê o `decision_json` anterior.

A classe e a versão do plano são gravadas juntas. A classe precisa existir na versão vigente do plano, no caminho assistido e em `DocumentCommands.ApplyClassificationAsync`. A versão documental usada é `current_version_id`. Zero linhas nesse predicado é conflito, sem escolher outra versão por conveniência.

Tags e metadados que não fazem parte da alteração permanecem. A classificação rápida do GED chama `SaveManualAsync` com tags e metadados nulos, e o serviço não apaga o que não foi enviado.

O processamento usa `RetentionRecalcService.RunOneAsync` na mesma conexão e transação que mantém `FOR UPDATE SKIP LOCKED` sobre a pendência. O bloqueio permanece durante recálculos superiores a 90 segundos. Recálculo, conclusão da pendência e remoção do estado parcial da revisão são atômicos. Falha reverte esses efeitos até um savepoint e persiste tentativa, erro sanitizado e próxima execução; perda da conexão reverte a transação. O prazo de 90 segundos serve somente para reconhecer claims legados abandonados.

O worker de temporalidade existente varre pendências a cada 30 segundos, em lotes de até 20 por tenant. O backoff vai de 60 a 3.600 segundos, com limite de dez tentativas automáticas. A recuperação manual `POST HospitalDocuments/RetryRetention` exige edição e permite nova tentativa após esse limite; `GET HospitalDocuments/RetentionPending` consulta o estado. Reaplicar revisão concluída não cria outra pendência. Metadados sem recálculo não aparecem como temporalidade concluída. HOLD, empréstimo e impedimento não são removidos pelo recálculo.
## Fontes, retenção e consumo

Fontes válidas são pares documento+versão em texto. Número, objeto, array, booleano, GUID vazio ou inválido, campo obrigatório ausente e pares inconsistentes são fonte corrompida. O codec não lança e não libera o resultado. Formato legado `{documento}/{versão}/texto-extraido` só é migrado depois que cada versão existe no tenant e o documento coincide. Resultado sem fonte comprovável fica inacessível. `result_expires_at` vale na primeira leitura e na primeira aplicação. O replay de uma revisão já gravada continua disponível depois da expiração e mostra a decisão humana, sem o texto do modelo. Resultado malformado, inclusive `Failure` textual, não produz HTTP 500 genérico.

`decision_json` e o detalhe de auditoria podem conter os valores que o revisor aplicou. Eles permanecem na linha porque são o rastro mínimo da decisão humana. Não entram em log. O JSON novo não guarda OCR nem evidência do modelo. O histórico só devolve esses campos a quem pode ver o documento agora. Conhecer `executionId` ou `applicationId` não amplia o acesso.

Quando uma estimativa já liquidada recebe uso real de uma execução enviada, a diferença entre o reportado e o liquidado ajusta o período original uma única vez (`reconciled_delta`, `usage_reconciled_at`). O valor liquidado original permanece. Reserva nunca enviada continua em zero. Conclusão válida não é substituída por falha posterior nem tem a retenção renovada por repetição.

## Telas

O parcial `_DocumentAiAssist` está no visualizador hospitalar, no painel lateral do GED e no detalhe do documento. Ele também lista o histórico de revisões e as pendências de temporalidade do documento, com paginação estável. As situações são: sugestão gerada, revisão registrada sem alteração, aplicação concluída, aplicação com recálculo pendente, pendência recuperada e resultado da IA expirado. Não há tela administrativa nova para esse histórico.

A administração nomeia as tarefas e explica as indisponíveis. Os scripts `document-summary.js` e `document-assist.js` usam delegação no documento, cancelam a espera, ignoram resposta fora de ordem e bloqueiam o segundo clique enquanto a gravação está pendente. Conflito e conclusão parcial aparecem com o texto do servidor. `retentionPending` ou `partial` mostra “Conclusão parcial”, inclusive quando a repetição devolve `alreadyApplied` e a pendência ainda existe. O texto exibido passa por escape.

Como usar: gere a sugestão, compare atual, sugerido e corrigido, confira a fonte pelo nome e pela versão e confirme só os campos escolhidos. Cancelar descarta a resposta atrasada. Tab percorre os controles. A nova tentativa de temporalidade fica na seção de pendências do próprio documento.

## Banco

A instalação nova utiliza o [schema base sem dados](../database/base/2026_05_schema.sql) e o [manifesto ordenado](../database/migrations.manifest.json), pelo comando `install --verify` do migrador. Instalações existentes usam `apply --verify`. O schema base inclui a estrutura legada de empréstimos; não é importado sobre dados existentes.

A migration publicada de recuperação de revisões permanece intacta. O [preflight de identidade](../database/migrations/2026_10_06_document_ai_review_identity_preflight.sql) deve ser executado antes dela, pois o backfill antigo excede 64 caracteres quando existem revisões equivalentes. A [migration de retry](../database/migrations/2026_10_07_document_ai_retention_retry.sql) adiciona agendamento persistente. O manifesto, o catálogo de migrations exigidas e as referências do consolidado estão sincronizados; isso não comprova execução do consolidado manual.

O diário oficial registra tentativas e checksum; recibos escritos por scripts legados usam tabela separada durante a execução oficial. `--retry-failed ID` exige o mesmo checksum e conserva a tentativa anterior. Os roteiros de instalação e upgrade, os resultados reproduzidos e os formatos históricos ainda não exercitados estão no [relatório operacional](ai-operational-acceptance.md).
## Homologação

Use banco e aplicativo locais, documentos fictícios e as variáveis de ambiente. Não use dump, paciente, credencial de produção ou segredo no repositório.

1. Instale pelo migrador em PostgreSQL descartável e teste também o upgrade legado. Preserve a ordem do manifesto, incluindo o preflight anterior à recuperação publicada.
2. Defina `INOVAGED_AI_PG_DSN` e rode `PostgresAiGovernanceStoreBehaviorTests` e `DocumentAiCyclePostgresTests`. Sem a variável, o fato é ignorado na descoberta: isso é não executado, não aprovação. As classes PostgreSQL compartilham uma collection sem paralelismo para impedir que o TRUNCATE da fixture de governança interfira nos demais cenários.
3. Para HTTP, defina `INOVAGED_AI_HTTP_BASE`, os perfis fictícios e os GUIDs do documento e da versão. Rode `scripts/homologation/document-ai-http.ps1`. Login com destino em `/Account/Login` não conta como autenticação. Sem a base, o script termina com código 2 e `NAO EXECUTADO`. Código 0 só ocorre quando a matriz do script passa. O provedor local é `Deterministic`, e só no ambiente `Homologation`, com `INOVAGED_AI_DETERMINISTIC=1` no processo da aplicação. Ele não chama HTTP e não homologa Groq, Gemini ou DeepSeek. A política do tenant precisa usar o mesmo provedor.
4. Repita com `GROQ_API_KEY`, `GEMINI_API_KEY` ou `DEEPSEEK_API_KEY` quando a credencial for válida.
5. Confira no navegador, em desktop e em largura estreita: resumo, preenchimento, tipo documental, classificação arquivística, histórico, pendência, cancelamento, duplo clique, conflito e conclusão parcial.

Recuperação: consulte pendências não resolvidas e sua próxima tentativa. O worker executa as elegíveis; quem edita o documento pode solicitar retry manual. Um consumidor não pode atravessar o bloqueio transacional de outro. Sucesso preenche `resolved_at` e remove o parcial na mesma transação. O erro persistido é sanitizado, sem texto documental.

Matriz mínima: sigilo não selecionado preservado; valor escolhido aplicado; redução sem `Security.Manage` negada; execução de outro documento ou tarefa negada; resultado expirado não reutilizado; repetição sem efeito duplicado; edição concorrente em conflito; tags e metadados não selecionados preservados; falha de auditoria revertida com a gravação; recálculo falho com conclusão parcial; legado sem fonte inacessível; consumo tardio reconciliado uma vez; tipo documental separado da classe do plano; campo sem evidência sem sugestão inventada; Pergunte ao acervo configurável; IA indisponível com GED e busca convencionais; outro tenant sem conteúdo nem aplicação.

## Evidência e pendências

O [relatório operacional](ai-operational-acceptance.md) mantém a comparação nominal com a baseline, os resultados PostgreSQL e HTTP, as limitações de instalação, a matriz por jornada e o gate do Protocolo institucional. Não se deve interpretar os roteiros existentes como jornadas aprovadas. Provedores reais, avaliação visual e uploads ainda exigem evidência própria.
