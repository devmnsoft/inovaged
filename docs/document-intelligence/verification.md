# Verificação da entrega inicial

Data: 20/10/2026. Os resultados abaixo distinguem testes aprovados, tentativas com falha e integrações ainda não homologadas. Não representam aceite das 15 sprints.

## Build e testes direcionados

`dotnet build InovaGed.sln --no-restore -v quiet`: **aprovado, 0 erros**. A solução ainda emite avisos de nulabilidade, chamadas obsoletas e advisories NuGet, incluindo PKCS, ImageSharp e OpenTelemetry. Eles não foram corrigidos por atualização indiscriminada de dependências nesta entrega.

`dotnet test InovaGed.Application.Tests/InovaGed.Application.Tests.csproj --no-build --no-restore --filter <classes abaixo>`: **78 aprovados, 0 falhas, 0 ignorados**.

| Classe | Aprovados |
|---|---:|
| OcrQueueRecoveryTests | 14 |
| OcrTranscriptIntegrityTests | 12 |
| OcrProcessLifetimeTests | 2 |
| MigrationManifestTests | 3 |
| OllamaDocumentProviderTests | 11 |
| AiProviderConnectivityTests | 3 |
| DocumentAiGatewayTests | 14 |
| AiProviderHomologationTests | 6 |
| GovernedDocumentAiGatewayReservationTests | 10 |
| DiCompositionTests | 1 |
| WebApiDependencyInjectionTests | 2 |

Os 14 testes de fila executaram SQL real no PostgreSQL 16 descartável, incluindo concorrência, fencing de lease, recuperação, tentativas, cancelamento, tenant, ausência de texto e idempotência das duas migrations. Não foram contabilizados como testes de reinicialização completa da aplicação nem como validação integral de ABAC clínico.

Os testes de processo iniciaram um PowerShell real em caminho com espaços/acentos/`#` e verificaram encerramento por timeout e cancelamento. A limpeza de diretório temporário é best-effort: a retenção de handles por indexação/antivírus no Windows não pode mascarar a asserção de término do processo.

## OCR real

Fixtures reproduzíveis são geradas por `scripts/homologation/generate-ocr-synthetic-fixtures.py`, com Pillow e pypdf. Todo conteúdo é sintético. O teste `OcrRealEngineTests` chama o serviço real com OCRmyPDF, Tesseract por/eng, Ghostscript, qpdf e Poppler configurados no ambiente; verifica texto e hash SHA-256 do arquivo de entrada.

A primeira execução completa encontrou **8 casos aprovados e 1 falha de qualidade na página inclinada**. O diagnóstico isolado demonstrou que segmentação em bloco e extração `-raw` recuperavam texto que a ordem geométrica fragmentava. O código foi corrigido para usar esse caminho uma única vez quando detecta fragmentação, registrando o método. Também foi ampliado o limite configurável de probes de ambiente após um falso negativo de inicialização fria.

O resultado da execução final das nove fixtures é registrado na atualização ao final deste arquivo. A presença da palavra esperada e a preservação do hash não comprovam transcrição clínica exata, estrutura de tabelas nem CER/WER. Manuscritos e assinaturas digitais criptográficas ainda não foram homologados no motor real; o teste de proteção de assinatura deste incremento usa a resposta controlada do processo.

## Provedores reais

- Gemini: chave aceita em `GET /v1beta/models`; armazenada somente como variável do usuário Windows. A geração sintética com `gemini-2.5-flash` falhou com modelo indisponível. Uma nova tentativa com `gemini-3.1-flash-lite`, listado no catálogo e com modalidade gratuita na documentação oficial, retornou HTTP 503/`ProviderUnavailable`. **Geração externa não homologada**.
- Groq: a chave fornecida foi recusada em `GET /openai/v1/models` com HTTP 401; nenhuma variável Groq foi alterada.
- Ollama: adaptador e políticas testados com transporte HTTP controlado; modelo real não instalado/executado nesta sessão.
- PaddleOCR: avaliação documental iniciada; integração/modelos e medição de português ainda pendentes.

Não foram enviados documentos de pacientes a provedores externos. As APIs não comprovam o plano efetivo da conta pela listagem de modelos. A política global `SensitiveDataPermitted=false` continua bloqueando dados sensíveis nas modalidades não autorizadas contratualmente.

## Regressão ampla e limites

A suíte completa da aplicação foi tentada e interrompida após registrar **31 falhas observadas**, incluindo contratos de interface/etiquetas e testes de integração de preview/Demais Setores. Não há resultado global aprovado nem contagem final dessa execução interrompida. Os três testes de composição que falharam nessa rodada foram diagnosticados, corrigidos e passaram na seleção final dos 78 testes. As demais falhas exigem diagnóstico próprio; não foram ocultadas nem marcadas como aprovadas.

`GroqFreeAssist` para expansão de consultas continua sendo um caminho legado sem contexto de tenant, desabilitado por padrão; não deve ser habilitado em conteúdo pessoal sem migração para a governança. O worker OCR deixou de usá-lo. O sintetizador antigo `OpenAiCompatibleSearchSynthesizer` também foi encontrado sem registro de DI ativo. A auditoria de todos os caminhos de egress, autenticação/iframe, criptografia e retenção permanece parte do aceite global pendente.

## Reprodução e evidências locais

1. Crie PostgreSQL descartável com nome de banco exato `inovaged_ocr_tests` e configure `INOVAGED_OCR_TEST_DSN`. Os novos testes recusam outro nome de banco.
2. Execute build e filtre as classes listadas acima. O TRX é `InovaGed.Application.Tests/TestResults/document-intelligence.trx`.
3. Gere fixtures: `python scripts/homologation/generate-ocr-synthetic-fixtures.py <pasta-temporaria>`.
4. Configure `INOVAGED_OCR_FIXTURE_DIR` com essa pasta e execute `dotnet test ... --filter FullyQualifiedName~OcrRealEngineTests`. O TRX final é `ocr-real-engine.trx`.
5. Para repetir o teste Gemini, escolha explicitamente um modelo atual em `INOVAGED_AI_LIVE_MODEL`, configure a chave no processo e `INOVAGED_AI_LIVE_SYNTHETIC=1`. O teste é opt-in e usa somente texto sintético; não habilita o processamento clínico.

Os logs de comandos e TRXs locais ficam em `artifacts/document-intelligence/` e `InovaGed.Application.Tests/TestResults/`, ignorados pelo Git. A auditoria e matriz de escopo estão em `audit-and-deliveries.md`. Nenhuma alteração foi publicada nem aplicada ao banco da aplicação.

## Resultado final do OCR real

**9 aprovados, 0 falhas, 0 ignorados**, em aproximadamente 4 minutos e 24 segundos, após as correções. Casos: PDF pesquisável, digitalizado, misto, inclinado, baixa resolução, tabela, acentos, campos ausentes e informações contraditórias. A página inclinada passou pelo caminho de recuperação; todos preservaram o SHA-256 do original. Somados aos testes direcionados, **87 testes aprovados** neste incremento.

Os contadores e hashes dos TRXs estão em `verification-results.json`; a relação completa de arquivos efetivamente alterados está em `changed-files.txt`. A regressão ampla e o teste externo Gemini continuam sem aprovação, conforme descrito acima.

Implementação verificada no commit `ce9eda703c754e97aa05a99fe171aaa50f79e944`.
