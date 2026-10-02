# Integração de IA documental

## Escopo e segurança

A integração é uma camada opcional sobre a recuperação autorizada do SmartSearch. `DocumentEvidenceService` continua selecionando o tenant, revalidando os documentos e produzindo os trechos; somente esses trechos são enviados ao provedor. Se IA estiver desabilitada, sem credencial ou sem evidência, a consulta convencional permanece disponível. Conteúdo documental é tratado como dado não confiável e jamais como instrução operacional.

O gateway não oferece comandos de protocolo, classificação, temporalidade, empréstimo ou eliminação. As tarefas `ExtractMetadata`, `SuggestClassification`, `SupportProtocol` e `CompareDocuments` são contratos para sugestões futuras/revisáveis; persistência e decisões continuam nos serviços canônicos e exigem confirmação humana. Não foi criada migration.

## Configuração segura

Configure `DocumentAi` por configuração protegida do servidor. Comece com `Enabled=false`, habilite explicitamente um provedor global, relacione cada tarefa a um modelo em `TaskModels` e inclua esse modelo em `AllowedModels`. A lista vazia falha de modo seguro e evita nomes de modelo antigos fixados no código.

As chaves são lidas somente no processo do servidor:

- `GROQ_API_KEY`
- `GEMINI_API_KEY`
- `DEEPSEEK_API_KEY`

Não coloque valores em `appsettings`, banco, logs ou frontend. A configuração não implementa fallback: uma chamada usa apenas o provedor selecionado. Restrições por tenant ainda devem ser conectadas ao cadastro administrativo antes de habilitação multitenant em produção; até lá, mantenha o recurso globalmente desabilitado quando tenants tiverem políticas distintas.

Exemplo conceitual (sem credenciais): habilite `DocumentAi:Enabled`, `DocumentAi:Providers:<provedor>:Enabled`, preencha `DocumentAi:TaskModels:AskCollection` e autorize o mesmo identificador em `AllowedModels` por variável de ambiente ou secret store do ambiente.

## Contratos verificados

Contratos implementados com os seguintes documentos oficiais como referência (a consulta automatizada no ambiente de entrega recebeu HTTP 403 em 2 de outubro de 2026, portanto a validação online deve ser repetida na homologação):

- Groq: API compatível com OpenAI em `POST /openai/v1/chat/completions`, autenticação Bearer. Consulte [API Reference](https://console.groq.com/docs/api-reference) e [Structured Outputs](https://console.groq.com/docs/structured-outputs).
- Gemini: `POST /v1beta/models/{model}:generateContent`, chave no cabeçalho `x-goog-api-key`; saída JSON é solicitada por `responseMimeType`. Consulte [Text generation](https://ai.google.dev/gemini-api/docs/text-generation) e [Structured output](https://ai.google.dev/gemini-api/docs/structured-output).
- DeepSeek: `POST /chat/completions`, autenticação Bearer e modo JSON por `response_format`. Consulte [Chat completion](https://api-docs.deepseek.com/api/create-chat-completion) e [JSON output](https://api-docs.deepseek.com/guides/json_mode).

Capacidades são do provedor e ainda precisam ser compatíveis com o modelo explicitamente selecionado. O catálogo é conservador: Groq e DeepSeek recebem apenas texto/OCR; Gemini pode aceitar imagem, mas o adaptador inicial envia apenas texto e não deve ser considerado multimodal homologado. Nenhum chat model é usado para embeddings.

## Homologação

1. Use tenant e usuário descartáveis e documentos fictícios sem dados pessoais.
2. Teste conexão sem acervo e confirme que chave inválida, 429, modelo ausente e timeout são classificados sem expor payload.
3. Para cada provedor/modelo permitido, teste pergunta sem resposta, negação, datas, documento extenso, OCR ruim e instrução maliciosa dentro do documento.
4. Confirme as referências contra documento e versão e verifique a autorização novamente após remover acesso.
5. Meça tokens reportados, duração e qualidade em português. Testes simulados não homologam o serviço real.
6. Não habilite extração, classificação, protocolo ou comparação em UI até haver orquestradores com schemas, revisão humana, auditoria e cotas por tenant/usuário.

## Estado da entrega

- **Verificado por implementação/testes unitários:** falha segura quando desabilitada; lista explícita de modelos; catálogo de capacidades; limites de entrada/saída; timeout/cancelamento; classificação padronizada de erros; segredos apenas em variáveis de ambiente; pergunta ao acervo preservando recuperação/autorização existente.
- **Parcial:** síntese textual com evidências; uso retornado pelos provedores; proteção contra prompt injection. O provedor não é chamado novamente automaticamente, mas idempotência persistente, retentativas e cotas distribuídas não estão implementadas.
- **Não homologado:** chamadas reais (o ambiente não forneceu credenciais); qualidade dos modelos; multimodal Gemini; painel administrativo/consumo; configuração por tenant; filas longas; resumo, extração, classificação, protocolo e comparação nas interfaces. Esses pontos não devem ser anunciados como disponíveis em produção.
