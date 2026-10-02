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

Não coloque valores em `appsettings`, banco, logs ou frontend. A configuração não implementa fallback: uma chamada usa apenas o provedor selecionado. Restrições por tenant ainda devem ser conectadas ao cadastro administrativo antes de habilitação multitenant em produção; até lá, mantenha o recurso globalmente desabilitado quando tenants tiverem políticas distintas. Esta configuração global **não constitui autorização de um cliente**.

`BaseUrl` precisa usar HTTPS e um host oficial (`api.groq.com`, `generativelanguage.googleapis.com` ou `api.deepseek.com`). Hosts adicionais somente são aceitos após inclusão explícita pela administração global em `DocumentAi:TrustedEndpointHosts`; URL com credenciais, query ou fragmento é rejeitada antes da leitura da chave e antes da rede.

Exemplo conceitual (sem credenciais): habilite `DocumentAi:Enabled`, `DocumentAi:Providers:<provedor>:Enabled`, preencha `DocumentAi:TaskModels:AskCollection` e autorize o mesmo identificador em `AllowedModels` por variável de ambiente ou secret store do ambiente.

## Contratos verificados

Contratos implementados com os seguintes documentos oficiais como referência (a consulta automatizada no ambiente de entrega recebeu HTTP 401 em 2 de outubro de 2026, portanto a validação online deve ser repetida na homologação):

- Groq: API compatível com OpenAI em `POST /openai/v1/chat/completions`, autenticação Bearer. Consulte [API Reference](https://console.groq.com/docs/api-reference) e [Structured Outputs](https://console.groq.com/docs/structured-outputs).
- Gemini: `POST /v1beta/models/{model}:generateContent`, chave no cabeçalho `x-goog-api-key`; saída JSON é solicitada por `responseMimeType`. Consulte [Text generation](https://ai.google.dev/gemini-api/docs/text-generation) e [Structured output](https://ai.google.dev/gemini-api/docs/structured-output).
- DeepSeek: `POST /chat/completions`, autenticação Bearer e modo JSON por `response_format`. Consulte [Chat completion](https://api-docs.deepseek.com/api/create-chat-completion) e [JSON output](https://api-docs.deepseek.com/guides/json_mode).

Capacidades comerciais do provedor e do modelo não são capacidades do adaptador. O catálogo exposto pela aplicação é deliberadamente conservador: os três adaptadores implementam texto e saída estruturada; não anunciam imagem, streaming nem embeddings. Em particular, bytes enviados ao adaptador Gemini textual são rejeitados antes da rede, em vez de descartados. A homologação deve ainda conferir se o modelo selecionado oferece o modo estruturado usado na tarefa.

Quando a tarefa fornece `OutputSchema`, Gemini recebe `responseJsonSchema`, Groq recebe `json_schema` estrito e DeepSeek recebe o modo JSON. Em todos os casos o resultado é validado localmente (tipos, obrigatórios, enumerações, itens e propriedades adicionais declaradas); JSON apenas sintaticamente válido não basta. Escolhas/candidatos ausentes, bloqueio, recusa e término por limite de tokens são rejeitados. Partes textuais do Gemini são concatenadas. A entrada completa e a resposta têm limites independentes.

## Homologação

1. Use tenant e usuário descartáveis e documentos fictícios sem dados pessoais.
2. Teste conexão sem acervo e confirme que chave inválida, 429, modelo ausente e timeout são classificados sem expor payload.
3. Para cada provedor/modelo permitido, teste pergunta sem resposta, negação, datas, documento extenso, OCR ruim e instrução maliciosa dentro do documento.
4. Confirme as referências contra documento e versão e verifique a autorização novamente após remover acesso.
5. Meça tokens reportados, duração e qualidade em português. Testes simulados não homologam o serviço real.
6. Não habilite extração, classificação, protocolo ou comparação em UI até haver orquestradores com schemas, revisão humana, auditoria e cotas por tenant/usuário.

## Estado da entrega

- **Verificado por inspeção e testes unitários adicionados:** falha segura quando desabilitada; lista explícita de modelos; capacidades efetivamente implementadas; bloqueio de endpoint arbitrário e binário; limite sobre entrada completa e resposta; cancelamento distinto de timeout; saída truncada/malformada/fora do schema rejeitada; correlação; segredos apenas em variáveis de ambiente. A pergunta ao acervo solicita afirmações estruturadas, aceita somente referências emitidas pelo servidor e revalida os documentos depois da chamada.
- **Bloqueado neste ambiente:** `dotnet` não está instalado, portanto restore/build/testes não foram executados aqui. A consulta automatizada às documentações oficiais também retornou HTTP 401. Esses checks precisam ser repetidos em CI/homologação; a descrição acima não é homologação dos provedores.
- **Parcial:** pergunta ao acervo com síntese, fontes e cobertura; uso retornado pelos provedores; proteção contra prompt injection. A validade estrutural e a presença de uma fonte não provam semanticamente cada afirmação, que permanece sujeita à revisão humana. O provedor não é chamado novamente automaticamente, mas idempotência persistente, retentativas e cotas distribuídas não estão implementadas.
- **Não implementado/não homologado:** chamadas reais (o ambiente não forneceu credenciais); política e cotas persistentes por tenant/usuário; registro persistente de execução; painel administrativo/consumo; resumo documental; preenchimento/classificação assistidos; filas longas; protocolo, comparação, multimodalidade e embeddings. Esses recursos não devem ser anunciados como disponíveis em produção.

## Evidências e comandos desta entrega

- Baseline confirmado antes das alterações: branch `work`, commit `25b3b95c52ab09a31268e21d7f3af49aaf27ea25`, árvore limpa.
- `dotnet test InovaGed.Application.Tests/InovaGed.Application.Tests.csproj --no-restore --filter FullyQualifiedName~DocumentAiGatewayTests`: bloqueado (`dotnet: command not found`).
- Nenhuma chamada real foi tentada porque não havia ferramenta .NET nem credenciais fornecidas. Nenhum teste de PostgreSQL ou validação visual foi executado; não há jornada nova completa para fotografar neste incremento.
