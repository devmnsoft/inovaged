# InovaGED Document Intelligence — auditoria e entregas

Data: 20/10/2026. Base inicial: `95fe0f53`. A árvore estava limpa no início. Nenhuma migration foi executada no banco da aplicação; os testes SQL usam exclusivamente o banco descartável `inovaged_ocr_tests`, contêiner `inovaged-ocr-audit-pg`, porta local 55442.

## Inventário encontrado

Os sete componentes solicitados existem. O worker registrado em `InovaGed.Web/Program.cs` é `OcrWorker`, condicionado a `OcrWorker:Enabled`. `OcrBackgroundService`, `OcrWorkerHostedService` e `OcrMyPdfProcessor` são implementações alternativas antigas e não estão registrados nesse host. O motor ativo é `IOcrService` → `OcrMyPdfOcrService`. Não foi removido código alternativo sem verificar seus demais consumidores.

| Área | Código existente examinado | Constatação |
|---|---|---|
| Domínio/aplicação | `InovaGed.Domain/Ged/OcrStatusEnum.cs`, `Application/Ocr`, `IOcrService.cs`, `Documents/DocumentAppService.cs` | Estados, contrato de fila e gravação de versão derivada já existem. |
| Motor/fila | `Infrastructure/Ocr`, `OcrMyPdfOcrService.cs` | Lease e heartbeat já existiam; recuperação de lease nulo, limite de crashes, propriedade do lease e extração vazia precisavam de correção. |
| Upload/processamento | `Infrastructure/Ged/Documents`, `OcrAutoSchedulerService`, `OcrAutoScheduleRepository` | Upload e agendamento existentes; estado COMPLETED isolado podia esconder ausência de texto. |
| Visualização/revisão | `Web/Controllers/OcrController.cs`, `Web/Views/Shared/_DocumentAiAssist.cshtml`, `DocumentAiAssistService`, `PostgresAssistedDocumentStore` | Há revisão, autorização e histórico. Isso não comprova o novo comparador OCR/IA por página/região. |
| IA/governança | `DocumentAiGateway`, `GovernedDocumentAiGateway`, `PostgresAiGovernanceStore`, `AiAdministrationController` | Já há Groq/Gemini/DeepSeek, cotas, execuções, idempotência e revisão; faltava Ollama e autorização distinta para egress sensível. |
| Pesquisa | `Infrastructure/SmartSearch`, `Infrastructure/Ged/Search`, `DocumentEvidenceService` | Busca textual, refinamento e evidências existentes; catálogo não anuncia embeddings. Não foi encontrado pipeline vetorial homologado. |
| Demais Setores | `Infrastructure/Sectors`, migrations de contexto/tickets/processamento/extração clínica | Há fluxos extensos posteriores ao prompt. Não foram reconstruídos nem declarados homologados nesta sessão. |
| API | `WebGed.WebApi` e referência no projeto de testes | Compila na verificação da solução; não foi criada API paralela para substituir serviços existentes. |

## Entrega 1 — correções operacionais OCR

- Aquisição conta tentativas duráveis; crashes não zeram o orçamento.
- Jobs PROCESSING com lease nulo ou expirado voltam a ser adquiridos; esgotamento passa a ERROR com `OCR_ATTEMPTS_EXHAUSTED`.
- `locked_by` existente contém identificador do consumidor. Renovação e mudanças de estado exigem propriedade e lease válido; resultados atrasados não sobrescrevem CANCELLED nem outro worker.
- A trava global é obtida antes da consulta de capacidade, evitando uso de snapshot anterior ao commit de outro consumidor. Enfileiramento serializa a combinação tenant/versão, inclusive solicitações forçadas.
- Heartbeat para de renovar após `MaxJobMinutes`. Perda de renovação cancela conversão/OCR.
- Retentativas OCR são persistidas em PENDING com `next_attempt_at`, liberando o worker durante backoff. O intervalo configurado cresce exponencialmente após a lista inicial.
- Falha de Poppler e texto vazio geram diagnóstico, em vez de sucesso sem pesquisa. A extração pede UTF-8 explicitamente.
- A homologação real detectou fragmentação na leitura geométrica de uma página inclinada. Uma heurística por página identifica excesso de linhas compostas de letras isoladas. O motor tenta uma única leitura adicional, desde a entrada íntegra, sem rotação cardinal, com segmentação em bloco (PSM 6), mantendo deskew e extraindo na ordem interna do PDF (`-raw`). O método de recuperação é registrado no resultado e na auditoria. Se a fragmentação persistir, retorna `OCR_TEXT_LOW_QUALITY` para conferência. A heurística é um sinal de revisão, sem alegação de exatidão; esse caminho pode perder espaços e não está homologado para todos os layouts.
- Cancelamento e timeout encerram a árvore do processo e aguardam a saída. Testes reais de processo usam diretórios com espaço, acentos e `#`.
- O texto OCR não é mais enviado ao `IFreeAssist` nem substituído silenciosamente por sua interpretação. Sugestões pertencem ao fluxo governado, com revisão.
- Logs de entrada não incluem nome/caminho do documento; stdout/stderr dos motores não são persistidos no diagnóstico do documento.
- COMPLETED sem texto não impede novo OCR; o agendador verifica presença de texto antes de considerar OCR disponível.

Migration aditiva: `2026_10_20_ocr_lease_recovery.sql`, registrada nos dois catálogos e no script consolidado. Reutiliza a tabela existente; adiciona compatibilidade e índices de recuperação. Não exclui jobs nem altera originais.

Arquivos de implementação: `Application/IOcrService.cs`, `Application/Ocr/OcrEnvironmentModels.cs`, `OcrTextQuality.cs`; `Infrastructure/Ocr/OcrOptions.cs`, `OcrJobRepository.cs`, `OcrProcessRunner.cs`, `OcrWorker.cs`, `OcrAutoSchedulerService.cs`, `OcrEnvironmentValidator.cs`; `Infrastructure/OcrMyPdfOcrService.cs`; validação de opções em `Infrastructure/DependencyInjection.cs`.

Testes: `OcrQueueRecoveryTests`, `OcrTranscriptIntegrityTests`, `OcrProcessLifetimeTests`, `OcrRealEngineTests`, `MigrationManifestTests`. O teste antigo de classificação foi corrigido para identificar o alias SQL `i` inteiro, sem falso positivo em `qi.title`.

Limitações: os consumidores antigos, se habilitados por outro host, ainda precisam de consolidação; a aquisição deduplica consumidores cooperantes pelo repositório, sem uma restrição única nova que poderia rejeitar duplicatas históricas. A gravação do artefato e a conclusão do job ainda não constituem uma única transação/outbox; interrupção nesse intervalo exige reconciliação posterior. A execução Linux integral e o conjunto de documentos assinados/manuscritos continuam pendentes.

## Entrega 2 — provedor local e proteção de egress

- Ollama nativo `/api/chat`, somente loopback, `stream=false`, schema JSON e tokens reportados; sem credencial externa.
- Catálogo de capacidades permanece honesto: adaptador textual, sem alegar reconhecimento visual ou embeddings.
- Respostas incompletas/truncadas e JSON fora do schema são rejeitados.
- Circuit breaker em memória por endpoint; nenhum reenvio automático de documento após resultado remoto incerto.
- Probes administrativos consultam catálogos atuais (`/models`, `/api/tags`), sem fallback fixo para modelo Gemini antigo. Descoberta não habilita modelos automaticamente.
- Gateway e diagnóstico compartilham validação de endpoint; redirects HTTP estão desabilitados.
- Documentos GED, fontes de Protocolo, evidências de pesquisa e extração clínica são tratados conservadoramente como sensíveis. Egress requer política explícita do tenant e referência de autorização formal. O administrador global também precisa atestar compatibilidade contratual do provedor; isso não concede acesso a documentos clínicos.
- Migration `2026_10_20_ai_sensitive_egress.sql`: políticas existentes começam com envio sensível bloqueado; constraint exige referência para habilitação.
- `AiProviderOptions.SensitiveDataPermitted` também começa falso: a política do cliente não pode autorizar uma modalidade contratual globalmente incompatível com dados sensíveis, como o serviço Gemini gratuito.
- A revisão da política e a referência de autorização de egress são copiadas para os metadados persistidos da execução, preservando a evidência mesmo após alteração da política.
- A validação de composição revelou registros compartilhados ausentes para `IGedAccessPolicyService`, `IFolderCommands`, fila OCR, fila de preview e repositório de status de preview; passaram a ser registrados com `TryAdd`, preservando as substituições dos hosts.

Arquivos: `Application/ArtificialIntelligence/AiProviderContracts.cs`; `Infrastructure/ArtificialIntelligence/{AiEndpointPolicy,AiProviderCircuitBreaker,DocumentAiGateway,GovernedDocumentAiGateway,PostgresAiGovernanceStore,DocumentAiAssistService}.cs`; propagação em `Protocolo/ProtocolAiAssistService.cs`, `Sectors/SectorClinicalExtractionService.cs`, `SmartSearch/DocumentEvidenceService.cs`; `DependencyInjection.cs`; controller/view administrativa e `appsettings.json` (sem segredos).

Testes: `OllamaDocumentProviderTests`, `GovernedDocumentAiGatewayReservationTests`, `DocumentAiGatewayTests`, `AiProviderHomologationTests`, migration de egress em PostgreSQL e `AiLiveSyntheticTests` opt-in. O adaptador Ollama é testado com respostas HTTP controladas; nenhum modelo Ollama foi executado nesta sessão. PaddleOCR ainda não é uma funcionalidade operacional entregue.

A chave fornecida foi aceita pelo catálogo Gemini e recusada pelo Groq (HTTP 401). Somente `GEMINI_API_KEY` foi configurada no ambiente do usuário Windows, fora do Git. Conteúdo clínico não foi enviado.

## Matriz do escopo completo

| Funcionalidade | Situação anterior | Implementação desta entrega | Evidência/testes | Status |
|---|---|---|---|---|
| 01. OCR recuperável | Lease existente com lacunas | Recuperação, tentativas, propriedade, backoff, extração e cancelamento | Testes de fila PostgreSQL, processo e transcrição | Incremento implementado; limitações acima |
| 02. Provedores | APIs textuais existentes | Ollama textual, catálogo, circuito e egress | Contratos HTTP e teste sintético opt-in | Parcial; Paddle/roteador/fallback pendentes |
| 03. Pipeline por página/região | Versões OCR e busca existentes | Preservação da transcrição e original reforçada | Hash em testes de motor real | Parcial; qualidade/comparação por região pendentes |
| 04. Extração hospitalar | Serviço clínico existente | Propagação de sigilo para governança | Compilação e testes aplicáveis | Catálogo completo não homologado |
| 05. Proveniência IA | Execuções e revisão existentes | Impedida substituição silenciosa no worker | Testes de transcrição | Comparador completo pendente |
| 06. Conferência profissional | Assistência/revisão existente | Sem reconstrução de viewer | Código inventariado | Novo viewer por região pendente |
| 07. SmartSearch | Texto/refinamento/evidências existentes | Egress sensível propagado | Testes de gateway | Embeddings/pesquisa semântica pendentes |
| 08. Classificação | Plano/revisão existentes | Preservados | Suítes existentes; sem nova homologação integral | Parcial |
| 09. Demais Setores | Tickets/contexto/extração existentes | Egress clínico propagado | Compilação | Homologação integrada pendente |
| 10. Banco/proveniência | Tabelas já existentes | Duas migrations aditivas, sem duplicar entidades | Idempotência e manifesto | Incremento implementado |
| 11. Administração | Política/cota/conectividade existentes | Catálogo local/remoto e autorização formal | Compilação; testes de política | Fallback e limites distribuídos pendentes |
| 12. Acervo antigo | Agendador e histórico existentes | Reprocessamento de COMPLETED sem texto e recuperação | Testes PostgreSQL | Pausar/retomar lote completo pendente |
| 13. Qualidade | Dashboards existentes | Entradas sintéticas reproduzíveis | Motor real opt-in | CER/WER e benchmark clínico pendentes |
| 14. Segurança | ABAC/tenant/revisão existentes | Egress local e externo explícito, sem segredos no Git | Testes negativos e migrations | Criptografia/retencão global não rehomologadas |
| 15. Aceite ponta a ponta | Suítes e scripts existentes | Novas suítes e gerador de fixtures | Resultados registrados em `verification.md` | Aceite final das 15 sprints não atingido |

Não tratar esta matriz como conclusão de todas as sprints. Próxima prioridade: reconciliar a persistência do artefato com o estado da fila, integrar PaddleOCR em processo isolado e medir português/páginas/regiões; em seguida comparar versões e implementar indexação semântica aprovada com isolamento por documento/tenant.
