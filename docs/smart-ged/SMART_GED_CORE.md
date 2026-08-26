# Smart GED Core — Inteligência Documental Operacional

## Objetivo
O Smart GED transforma OCR e metadados já autorizados em apoio operacional local, sem enviar conteúdo a terceiros e sem alterar decisões arquivísticas sem confirmação humana.

## Fluxo operacional
`Documento > OCR/texto disponível > regras locais > sugestão > revisão humana > aceite/rejeição > auditoria`. A análise manual está disponível em `/SmartGed/Document/{id}` e a fila em `/SmartGed/ReviewQueue`.

## Tabelas criadas
`document_ai_analysis` preserva o resultado; `document_classification_suggestion` e `document_retention_suggestion` preservam propostas e revisão; `document_quality_issue` registra pendências; `smart_search_query_log` registra consultas. Todas ficam no schema `ged`, possuem isolamento por `tenant_id` e são criadas pela migration idempotente `2026_08_26_smart_ged_core.sql`.

## Serviços criados
Os contratos de análise, extração, classificação, temporalidade e busca ficam em `InovaGed.Application/SmartGed`. `SmartGedService` implementa persistência, busca, revisão e auditoria; `LocalDocumentMetadataExtractor` implementa a extração sem dependência externa.

## Heurísticas locais
Expressões regulares reconhecem CPF, CNPJ (sempre persistidos/exibidos mascarados), processo, protocolo e datas. Vocabulários identificam documentos fiscais, pessoais, contratos e saúde, derivando tipo, assunto, palavras-chave, sensibilidade e confiança.

## Classificação sugerida
Palavras-chave são comparadas com código, título e descrição do plano do tenant. A melhor correspondência gera uma sugestão pendente. Plano ausente gera problema de qualidade, nunca uma classificação definitiva.

## Temporalidade sugerida
A sugestão usa a classificação candidata e destino final disponível. Ausência de regra clara permanece como `REQUER_REVISAO`. O núcleo não elimina documentos e não executa destinação automática.

## Busca inteligente
A busca local cobre título, texto extraído, resumo e classificação sugerida, limita resultados, filtra obrigatoriamente pelo tenant e grava tempo/quantidade no log. Novas fontes físicas, QR e etiquetas podem ser adicionadas quando o schema correspondente estiver disponível.

## Fila de revisão
Operadores aceitam ou rejeitam propostas individualmente, com antiforgery e estado monotônico: apenas sugestões `PENDING` podem ser revistas. Ações são auditadas.

## Segurança/LGPD
Não há chamada de IA externa. CPF/CNPJ são mascarados antes da persistência estruturada e nunca aparecem completos em listagens. Consultas e mutações incluem `tenant_id`; acesso requer autenticação. O texto integral continua restrito aos detalhes autorizados.

## Como validar
1. Execute a migration Smart GED Core em `/DatabaseReadiness`.
2. Abra `/SmartGed`, analise um documento do tenant e confira os metadados.
3. Aceite/rejeite sugestões na fila, execute uma busca e confira auditoria e qualidade.
4. Valide `/SchemaHealth`, `/Administration` e compile `InovaGed.sln`.

## Pendências futuras para IA externa
Provedores externos serão opcionais, sujeitos a consentimento, minimização, redaction, residência de dados, contrato LGPD e trilha de versão do modelo. Também são evoluções previstas: ranking semântico local, extração de pessoas com dicionários por tenant e regras formais de retenção condicionadas a eventos.
