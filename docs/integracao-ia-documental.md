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

O token de concorrência é `xmin` do documento, devolvido na sugestão e exigido na gravação. A alteração, o registro em `ged.ai_suggestion_application` e a linha em `ged.app_audit_log` ocorrem na mesma transação. Repetir a mesma decisão não cria outra auditoria nem recalcula temporalidade. Decisão diferente com token vencido responde conflito e não sobrescreve.

Tags e metadados que não fazem parte da alteração permanecem. A classificação rápida do GED chama `SaveManualAsync` com tags e metadados nulos, e o serviço não apaga o que não foi enviado.

O recálculo de temporalidade ocorre depois do commit. Se a classificação foi gravada e o recálculo falhou, a resposta é conclusão parcial e a pendência vai para `ged.ai_retention_recalc_pending`. A interface só mostra sucesso depois da resposta persistida.

## Fontes, retenção e consumo

Fontes válidas são pares documento+versão. JSON inválido é dado corrompido e não libera fallback. Formato legado `{documento}/{versão}/texto-extraido` só é migrado depois que cada versão existe no tenant e o documento coincide. Resultado sem fonte comprovável fica inacessível. `result_expires_at` vale na consulta, na aplicação e na reutilização por idempotência. Resultado malformado, inclusive `Failure` textual, não produz HTTP 500 genérico.

Quando uma estimativa já liquidada recebe uso real de uma execução enviada, a diferença entre o reportado e o liquidado ajusta o período original uma única vez (`reconciled_delta`, `usage_reconciled_at`). O valor liquidado original permanece. Reserva nunca enviada continua em zero. Conclusão válida não é substituída por falha posterior nem tem a retenção renovada por repetição.

## Telas

O parcial `_DocumentAiAssist` está no visualizador hospitalar, no painel lateral do GED e no detalhe do documento. A administração nomeia as tarefas e explica as indisponíveis. Os scripts `document-summary.js` e `document-assist.js` usam delegação no documento, cancelam a espera, ignoram resposta fora de ordem e bloqueiam o segundo clique enquanto a gravação está pendente. Conflito e conclusão parcial aparecem com o texto do servidor.

Como usar: gere a sugestão, compare atual, sugerido e corrigido, confira a fonte pelo nome e pela versão e confirme só os campos escolhidos. Cancelar descarta a resposta atrasada. Tab percorre os controles.

## Banco

Migration incremental `database/migrations/2026_10_05_document_ai_application_integrity.sql`. Ela adiciona `usage_reconciled_at` e `reconciled_delta` em `ged.ai_execution`, cria `ged.ai_suggestion_application` e `ged.ai_retention_recalc_pending`, e está registrada no manifesto, em `required_migrations.json` e uma vez em `apply_all_required_migrations.sql`. As migrations anteriores não foram reescritas.

## Homologação

Use banco e aplicativo locais, documentos fictícios e as variáveis de ambiente. Não use dump, paciente, credencial de produção ou segredo no repositório.

1. Aplique as migrations de IA em PostgreSQL descartável. A `2026_10_05` é idempotente.
2. Defina `INOVAGED_AI_PG_DSN` e rode `PostgresAiGovernanceStoreBehaviorTests`. Sem a variável, o fato é ignorado na descoberta: isso é não executado, não aprovação.
3. Para HTTP, defina `INOVAGED_AI_HTTP_BASE`, usuário, senha e os GUIDs fictícios do documento e da versão. Rode `scripts/homologation/document-ai-http.ps1`. Sem a base, o script termina com código 2 e a mensagem `NAO EXECUTADO`. O arquivo `scripts/homologation/document-ai-fictional-fixture.sql` apenas orienta a massa fictícia; não entra nas migrations obrigatórias.
4. Repita com `GROQ_API_KEY`, `GEMINI_API_KEY` ou `DEEPSEEK_API_KEY` quando a credencial for válida. Mock HTTP não homologa provedor.
5. Confira no navegador, em desktop e em largura estreita: resumo, preenchimento, tipo documental, classificação arquivística, cancelamento, duplo clique, conflito e conclusão parcial. Confira também upload, protocolo, custódia e temporalidade.

Matriz mínima: sigilo não selecionado preservado; valor escolhido aplicado; redução sem `Security.Manage` negada; execução de outro documento ou tarefa negada; resultado expirado não reutilizado; repetição sem efeito duplicado; edição concorrente em conflito; tags e metadados não selecionados preservados; falha de auditoria revertida com a gravação; recálculo falho com conclusão parcial; legado sem fonte inacessível; consumo tardio reconciliado uma vez; tipo documental separado da classe do plano; campo sem evidência sem sugestão inventada; Pergunte ao acervo configurável; IA indisponível com GED e busca convencionais; outro tenant sem conteúdo nem aplicação.

## Verificação desta sessão

| Verificação | Resultado |
|---|---|
| Build de `InovaGed.Web` e `InovaGed.Application.Tests` | passou, 0 erros |
| `DocumentAiIntegrityTests` e testes de reserva do gateway, inclusive replay expirado ou malformado sem chamar o provedor | passou |
| `MigrationManifestTests` dentro da suíte geral | passou |
| PostgreSQL descartável com migrations `2026_10_02` a `2026_10_05`, reaplicação da `2026_10_05` | passou |
| `PostgresAiGovernanceStoreBehaviorTests` com `INOVAGED_AI_PG_DSN` nesse banco | passou, 16/16 |
| Mesmos fatos PostgreSQL na suíte geral, sem DSN | não executado (16 ignorados) |
| Suíte `InovaGed.Application.Tests` | 525 aprovados, 36 falhas, 17 ignorados |
| Falhas da suíte | as 36 são contratos já existentes de etiquetas, shell visual, identidade, composição de DI, Web API e segredo em configuração; nenhuma está nas jornadas de IA alteradas |
| `scripts/homologation/document-ai-http.ps1` sem `INOVAGED_AI_HTTP_BASE` | não executado, código 2 |
| Credenciais Groq, Gemini e DeepSeek | ausentes; prova real de provedor não executada |
| `apply_all_required_migrations.sql` em PostgreSQL vazio | bloqueada por defeito anterior: índice `ix_upload_batch_last_problem_user` usa `created_by` antes de a coluna existir; depois do ajuste manual só no banco descartável, parou em `ged.loan_request` inexistente. A migration nova não chegou a ser executada por esse script consolidado |
| Navegador, desktop e largura estreita | não executado: não há ferramenta de navegador nesta sessão e o aplicativo não foi iniciado |
| Produção | não alterada |

## Pendências

- Corrigir a instalação limpa do script consolidado sem reescrever migrations já aplicadas.
- Homologar HTTP com perfis leitor, editor, sigilo e outro tenant em aplicativo local.
- Homologar Groq, Gemini ou DeepSeek com credencial válida.
- Conferir as telas no navegador, inclusive foco, teclado e largura estreita.
- Protocolo assistido, comparação, voz e multimodalidade.
