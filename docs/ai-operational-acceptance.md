# Auditoria operacional — GED e assistência documental

Base auditada: `8f784ba5e64f3fe9345c98beecb368de84db77b7`. O checkout inicial estava nessa revisão, sem alterações locais; não havia commits posteriores no checkout. Não foi realizada publicação nem execução em banco de produção.

## Critério de aceite

O avanço para decisões assistidas no Protocolo institucional depende de instalação/upgrade, autorização por registro, idempotência, recuperação e matriz HTTP local aprovados. O gate permanece **fechado** enquanto faltar qualquer evidência obrigatória. Código presente, documentação histórica e testes estáticos não equivalem a homologação operacional.

O TR original ainda não foi localizado. A numeração funcional abaixo não inventa correspondência com requisitos contratuais. RF01 fica excluído do escopo funcional de Protocolo; autorização continua transversal. `/Protocolo` institucional, `/Protocols` administrativo e empréstimos são jornadas diferentes.

## Evidências reproduzidas

Os resultados ficam em `artifacts/ai-operational/` (arquivos locais, não versionados):

- Baseline completo: **529 aprovados, 36 falhas, 20 ignorados**, 585 testes. `baseline/baseline.trx`.
- PostgreSQL, assistência e autorização: **56 aprovados, nenhuma falha ou teste ignorado**. `postgres.trx`. Inclui consumidor concorrente após 92 segundos, rollback após falha na conclusão, cancelamento, backoff, intervenção manual, replay, preservação de HOLD e compatibilidade de assinatura legada. Essa execução antecede os últimos ajustes de autorização em lote e de estado do replay; é necessário repeti-la sobre a versão final.
- Comparação final sem DSN PostgreSQL: **530 aprovados, 36 falhas, 28 ignorados**, 594 testes. `after/after.trx`. A comparação nominal com o TRX da baseline não encontrou falha nova nem falha removida. A inconsistência intermediária entre manifesto e consolidado foi corrigida. Testes ignorados não contam como aprovação.
- Falha legada de identidade reproduzida como SQLSTATE `22001`; upgrade testado com zero, uma, múltiplas revisões equivalentes, decisões diferentes e preenchimento parcial. Histórico e vínculos de auditoria preservados.
- Instalação limpa pelo migrador oficial e replay passaram com 122 migrations; o aplicativo iniciou e passou sua verificação de schema. A migration posterior de ações de auditoria foi aplicada e verificada, totalizando 123. Uma instalação vazia com as 123 ainda precisa ser repetida. O consolidado manual não foi homologado nesta rodada.
- Upgrade pelo migrador oficial passou: o roteiro reproduziu SQLSTATE `22001`, preservou três revisões e seus vínculos de auditoria, aplicou o preflight, repetiu a migration publicada com o mesmo checksum e preservou as tentativas FAILED/APPLIED. O replay completo passou. `upgrade-result.txt` e logs de upgrade.
- HTTP em Kestrel local exercitou login real, antiforgery, aplicação/histórico/replay, conflito, isolamento de usuário/tenant, ACL, endpoints de leitura, sigilo, expiração, fontes inválidas, versão, ausência de OCR e recuperação automática pelo worker após falha injetada. Uma execução completa teve uma asserção incorreta: exigia `retention_status=HOLD`, mas o bloqueio canônico é `retention_hold=true`; flag e motivo permaneceram intactos. A asserção foi corrigida. A repetição foi interrompida porque o Docker Desktop foi pausado manualmente. **Ainda não há execução final integral com código 0.**
- Navegador aberto na tela de login, sem conclusão da jornada. Isso não comprova validação visual, responsividade nem acessibilidade. Nenhum provedor externo foi chamado.

## Matriz de jornadas

| Requisito/jornada | Implementação | Evidência atual | Status | Pendência |
|---|---|---|---|---|
| Identidade de revisão e upgrade legado | Preflight antes da migration publicada; chave canônica SHA-256 e identidades secundárias de 64 caracteres | Testes PostgreSQL e roteiro completo pelo migrador oficial, com falha/retry/replay e preservação do diário | implementado com evidência | Formatos históricos de diário não representados pela fixture |
| Retenção após revisão | Worker existente executa pendências; transação única para recálculo, revisão e conclusão | Testes PostgreSQL, inclusive duração superior a 90 segundos; recuperação automática observada por HTTP após falha injetada | implementado com evidência | Reinício abrupto do processo hospedado e repetição final da matriz HTTP |
| Falha persistente | Tentativas, próxima execução, mensagem sanitizada; limite automático e recuperação manual | Testes de falha, cancelamento, backoff e décima tentativa | implementado com evidência | Evidência operacional pela UI |
| GED por registro | Permissão de módulo + tenant/usuário ativo + ACL; autorização em lote nas listas; caches compartilhados removidos | Testes PostgreSQL e HTTP de leitor, ausência de ACL, outro tenant, viewer/preview/OCR e listas após aquecimento por usuário autorizado | parcial | Repetição sobre a última versão; demais endpoints GED e janela de horário do sigilo |
| Protocolo por setor | Serviço canônico exige setor ativo; administrador ainda exige registro no tenant | Testes PostgreSQL | parcial | Matriz HTTP institucional e regressão dos comandos |
| Vínculos GED/Protocolo | Vincular/remover exige gerenciamento do protocolo e VIEW/EDIT documental; consulta filtra VIEW | Compilação; testes de política subjacente | parcial | Testes HTTP, teclado e verificação visual |
| Central de tramitação | Visibilidade do selecionado e filtro documental nas referências, pesquisa e custódia física | Compilação | parcial | Teste integrado da central e avaliação de custo da autorização de referências |
| Instalação limpa | Schema base sem dados + manifesto ordenado + histórico/checksum transacional | Instalação e replay oficiais com 122 migrations, aplicação incremental da 123 e startup real | parcial | Repetir instalação vazia final com 123; validar consolidado manual |
| Histórico de migrations | Checksum dos arquivos publicados preservado; recibos legados separados do diário de tentativas | Upgrade oficial com falha/retry de mesmo checksum e replay aprovado | parcial | Upgrade de outros formatos históricos do diário; teste de checksum divergente |
| HTTP local obrigatório | Fixture executável; login real, 403/404 e identidade exata da revisão; opcionais separados | Cenários executados; repetição final interrompida pelo Docker pausado | parcial | Execução final integral com código 0; Protocolo institucional e reinício abrupto |
| Provedores reais | Adaptadores existentes; Deterministic limitado a Homologation com flag explícita | Teste bloqueia Deterministic em Production | não verificado | Credenciais e chamadas reais Groq/Gemini/DeepSeek; credenciais ausentes nesta sessão |
| Assistência institucional | Gate obrigatório preservado | Nenhuma nova decisão assistida liberada | parcial | Resumo, lacunas, assunto e minuta com revisão humana após gate A |
| Uploads individual/lote | Fluxos existentes preservados | Suíte geral com falhas preexistentes; sem jornada real executada | não verificado | Sucesso, parcial, retry, duplicata e progresso via HTTP/UI |
| Acessibilidade e layout | Busca/seleção por nome e orientação contextual nos vínculos | Código Razor compila | não verificado | Desktop/estreito, Tab, foco, leitura e contraste no navegador |
| TR/PoC completo | Documentação histórica localizada | TR original não confirmado | não verificado | Confirmar fonte e mapear cláusulas sem inferir aceite |

## Cenários de aceite e resultado esperado

| Cenário | Resultado esperado | Evidência e situação |
|---|---|---|
| Duplicatas legadas | Upgrade sem perda de revisão ou vínculo | PostgreSQL e upgrade oficial aprovados; implementado com evidência |
| Instalação limpa | Fluxo oficial chega ao final e admite replay | 122 migrations em banco vazio e 123ª incremental; parcial até repetir o conjunto final |
| Queda após commit | Worker encontra o trabalho após reinício do processo | Pendência durável e consumidor implementados; reinício abrupto não verificado |
| Consumidores concorrentes | Uma conclusão consistente, sem efeitos duplicados | Teste PostgreSQL com processamento superior a 90 segundos; implementado com evidência |
| Usuário sem acesso | Nenhum conteúdo documental enviado à IA | Política e recusas HTTP exercitadas; parcial, falta repetir versão final e matriz institucional |
| Outro tenant | Nenhum registro ou metadado exposto | HTTP documental e testes PostgreSQL; parcial para a jornada institucional |
| Fonte alterada | Resultado invalidado ou revalidado antes de aplicação | HTTP responde 409 após alteração da versão vigente; implementado com evidência no fluxo documental |
| IA indisponível | GED, busca e Protocolo convencionais continuam disponíveis | Não verificado por indisponibilidade induzida em jornada real |
| Aplicação repetida | Mesma revisão, sem nova auditoria ou recálculo | Testes PostgreSQL e HTTP de identidade/histórico; implementado com evidência documental |
| Decisão diferente na mesma identidade | Conflito sem sobrescrever a revisão | HTTP 409 e testes de identidade; implementado com evidência |
| Despacho assistido | Revisão humana e aplicação por comando canônico | Bloco B não implementado; não verificado |
| Upload parcialmente concluído | Situação e erro por arquivo/etapa, sem sucesso integral falso | Jornada real não verificada |
| Documento impedido | Destinação bloqueada com motivo preservado | HOLD preservado no recálculo; destinação com empréstimo/tramitação ativa não verificada nesta rodada |

O [complemento HTTP institucional](../scripts/homologation/document-ai-protocol-http.ps1) foi preparado e incluído na matriz obrigatória: acesso por setor, ACL no seletor, revogação de edição, outro tenant e vínculo/remoção com auditoria e preservação do documento. Sua sintaxe PowerShell foi validada, mas os cenários ainda não foram executados. Essa preparação não altera o status de aceite.

## Comandos e reprodução

Comandos executados nesta sessão, a partir da raiz do repositório:

```powershell
dotnet test InovaGed.Application.Tests/InovaGed.Application.Tests.csproj --no-restore --verbosity quiet --logger 'trx;LogFileName=after.trx' --results-directory artifacts/ai-operational/after -p:WarningLevel=0 -m:1
```

Também foram executados os roteiros de instalação, upgrade, preparação PostgreSQL, inicialização local e matriz HTTP vinculados abaixo. Os testes PostgreSQL usaram `INOVAGED_AI_PG_DSN` apontando exclusivamente ao banco descartável local, com filtro `FullyQualifiedName~DocumentAi|FullyQualifiedName~AiGovernance|FullyQualifiedName~ReviewIdentity|FullyQualifiedName~MigrationManifestTests`. A suíte geral final foi executada sem essa variável; seus testes ignorados são explicitados acima. `git diff --check` e a análise sintática dos scripts PowerShell passaram. Os builds emitiram o aviso preexistente NU1902 sobre OpenTelemetry 1.9.0; não houve atualização de dependências neste escopo.

## Instalação e limites

O novo `install` do migrador aceita apenas schema GED vazio (exceto seu diário). Carrega [schema base](../database/base/2026_05_schema.sql), extraído das definições do dump textual já existente, sem COPY, dados de usuários, proprietários ou grants. Depois aplica o [manifesto](../database/migrations.manifest.json). O schema base não substitui migrations incrementais nem é importado sobre uma instalação existente.

Use `apply` para upgrade. Uma tentativa transacional FAILED só pode ser repetida com `--retry-failed ID`, mantendo o mesmo checksum e preservando o registro anterior. Divergência de checksum continua bloqueada. Não se deve apagar histórico para contornar falhas. Scripts publicados não foram alterados; a execução remove somente BEGIN/COMMIT externos reconhecidos e direciona recibos legados ao diário separado, mantendo o checksum do texto original.

O roteiro [test-document-ai-install.ps1](../scripts/homologation/test-document-ai-install.ps1) cria banco novo no container local `inovaged-ai-operational-pg`, porta 55439, e executa instalação/verificação/replay. Esses bancos contêm apenas schema e dados sintéticos. O roteiro [prepare-document-ai-postgres.ps1](../scripts/homologation/prepare-document-ai-postgres.ps1) prepara testes específicos de IA e **não** comprova instalação completa.

O roteiro [test-document-ai-upgrade.ps1](../scripts/homologation/test-document-ai-upgrade.ps1) cria outro banco descartável e reproduz falha e recuperação pelo migrador oficial. [start-document-ai-local.ps1](../scripts/homologation/start-document-ai-local.ps1) inicia o aplicativo de homologação na porta 5189; [run-document-ai-local-http.ps1](../scripts/homologation/run-document-ai-local-http.ps1) executa a matriz com perfis fictícios. Execute o primeiro em processo PowerShell próprio; ele configura variáveis do processo. Os roteiros locais usam credenciais sintéticas, não devem apontar para produção e não homologam provedores externos.

Bloqueio atual: Docker Desktop pausado manualmente; a retomada foi consultada ao usuário e não foi executada sem resposta. O aplicativo iniciado nesta sessão foi encerrado. O gate A permanece fechado; as funcionalidades novas do bloco B não foram implementadas nem liberadas.

Antes de atualizar o worker em instalação existente, encerre os consumidores da versão anterior: o novo processamento mantém bloqueio transacional, enquanto uma versão antiga ainda pode usar o lease expirável. HOLD não é removido; a recuperação não autoriza destinação.

## Fora deste aceite

Voz/multimodal, comparação documental, pedidos administrativos, notas fiscais, assinatura ICP e PACS permanecem em backlog separado. Compatibilidade de schema de assinatura não homologa certificado, cadeia de confiança nem assinatura real. Falhas preexistentes de UI, composição DI e configuração permanecem registradas no baseline; nenhuma tela é considerada visualmente homologada por teste textual.
