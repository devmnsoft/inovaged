# Smart GED Assistant

## Objetivo
O Assistente Documental responde perguntas operacionais exclusivamente com evidências recuperadas no tenant autenticado, sem depender de LLM externo.

## Fluxo de conversa
O usuário inicia uma sessão, envia uma pergunta, o retrieval consulta fontes disponíveis no schema e o compositor produz uma resposta. Pergunta, resposta, fontes e ações recomendadas são persistidas e auditadas.

## Fontes consultadas
Documentos, análises OCR/IA, sugestões de classificação e temporalidade, problemas de qualidade, caixas físicas, etiquetas e históricos de impressão, planos de classificação e regras de retenção. Fontes ausentes produzem avisos em vez de erro ou resposta inventada.

## Citações
Cada evidência registra tipo, identificador, título, trecho mascarado, confiança e link interno somente quando existe uma rota segura conhecida.

## Ações sugeridas
Ações nascem `PENDING`. Aceitar ou rejeitar apenas registra a revisão humana; o assistente não classifica, elimina, altera temporalidade ou executa OCR automaticamente.

## Segurança e LGPD
Toda consulta inclui `tenant_id`. CPF e CNPJ são mascarados em perguntas, respostas e citações. Segredos e detalhes técnicos não fazem parte das fontes. A trilha de auditoria registra sessões, perguntas, respostas, citações e decisões.

## Limitações da versão local
A busca usa correspondência textual e schema discovery. Ela não realiza inferência semântica avançada e informa honestamente quando não existe evidência suficiente.

## Como validar
Aplique `2026_08_26_smart_ged_assistant.sql` em `/DatabaseReadiness`, confira `/SchemaHealth`, inicie uma conversa em `/SmartAssistant` e valide fontes, confiança, mascaramento e sugestões pendentes.

## Futuras integrações com LLM externo
Um compositor externo poderá ser opcional, desde que receba somente evidências autorizadas e mascaradas, preserve citações e tenha fallback local.
