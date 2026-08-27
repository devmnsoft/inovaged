# Classification & Retention Plan 2.0

## Objetivo
Centralizar, com isolamento por tenant, a árvore do plano de classificação (PCD), a tabela de temporalidade (TTD), revisão e publicação de snapshots imutáveis.

## Rotas
`/ClassificationPlan` apresenta o dashboard; `/Tree`, `/Create` e `/Edit/{id}` mantêm a árvore; `/RetentionRules` e `/RetentionRule/{id}` mantêm regras; `/Versions` publica; `/Compare` compara; `/Import` valida e confirma CSV; `/ReviewQueue` reúne pendências. Todas exigem autenticação e todos os POST usam antiforgery.

## Banco de dados
A migration `2026_08_27_classification_retention_plan_2.sql` é aditiva e idempotente. Cria `classification_node`, `retention_rule_v2`, `classification_plan_version_v2` e `classification_change_request`, com índices por tenant e sem remover os instrumentos legados.

## Árvore de classificação
Classes possuem código único no tenant, classe pai selecionada por catálogo, atividade MEIO/FIM, função, fonte normativa, palavras-chave, ordem e status. O serviço impede pai inexistente, autorreferência e ciclos.

## Regras de temporalidade e eventos condicionantes
Cada classe pode ter uma regra ativa com fases corrente e intermediária, destino final, evento e descrição, base legal, observações e vigência. Prazos negativos, vigência invertida e `AGUARDANDO_EVENTO` sem evento são recusados.

## Versionamento e comparação
Publicar gera um snapshot JSON transacional de classes e regras, incrementa a versão e preserva publicações anteriores. A comparação usa os snapshots e identifica itens adicionados, removidos e alterados.

## Importação
O CSV esperado contém `code,title,parent_code,description,activity_type,keywords,current_phase_years,intermediate_phase_years,final_destination,trigger_event,legal_basis`. O primeiro POST apenas valida e pré-visualiza; confirmação explícita é obrigatória e não apaga o plano atual.

## Revisão
A fila reúne classes e regras em rascunho, destaca classes sem regra e base legal ausente. Aprovação e publicação permanecem ações separadas.

## Integrações
A árvore e as regras expostas pelos serviços são a fonte para Documento 360 e Smart GED. As pendências exibem tipos compatíveis com Smart Workflow (`CLASSIFICATION_REVIEW`, `RETENTION_RULE_REVIEW`, `CLASSIFICATION_IMPORT_REVIEW`, `LEGAL_BASIS_REVIEW`). A aplicação nunca aceita sugestão inteligente automaticamente.

## Segurança
Consultas e alterações incluem `tenant_id`; não há exclusão física. Os formulários não expõem digitação de UUID: classes e versões são selecionadas por código/título. POSTs são protegidos por antiforgery.

## Como validar
Aplique migrations obrigatórias, compile a solução e percorra todas as rotas descritas. Crie raiz e subclasse, vincule uma regra, publique duas versões, compare, valide um CSV e abra a fila.

## Pendências futuras
Adicionar parser CSV com aspas multilinha, aprovação em lote e conectores configuráveis para vocabulários externos.
