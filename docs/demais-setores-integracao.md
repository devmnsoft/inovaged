# Demais setores — abertura para sistemas parceiros

Este contrato é do InovaGed. Ele não descreve um endpoint nativo do MV/Soul. A origem clínica continua desligada até `ClinicalSource:Enabled`, `BaseUrl` e `PatientPath` serem configurados com um contrato publicado. A chave dessa origem fica na variável de ambiente `INOVAGED_CLINICAL_SOURCE_KEY` e não entra em log nem em JavaScript.

## Entrada por URL

Sistemas que só conseguem abrir um endereço HTTPS usam:

`GET /DemaisSetores/Integrar?integracao=parceiro-exemplo&tipoIdentificador=ID&paciente=000120&codigoAtendimento=000088&nomeDocumento=Prescri%C3%A7%C3%A3o%20M%C3%A9dica&modo=consulta`

Parâmetros obrigatórios: `integracao`, `tipoIdentificador` (`ID` ou `CPF`), `paciente`, `codigoAtendimento`. Opcionais: `nomeDocumento` (até 160 caracteres, com espaço e acento), `modo` (`consulta` ou `documento`), `codigoDocumento` (até 16), `documentoId`, `versaoId` e `destino` (`pagina`, padrão, ou `iframe`). `destino=iframe` exige `origemIncorporacao`, contendo somente uma origem HTTP/HTTPS cadastrada. Origem com destino de página é contraditória e é recusada. Outro parâmetro, repetição ou GUID vazio torna o link inválido. Zeros à esquerda do ID e do atendimento são preservados. CPF é normalizado e validado; não autentica.

O tenant, o sistema e a unidade vêm do cadastro da integração. A resposta dessa entrada não traz conteúdo clínico. Ela grava o contexto no servidor, devolve um identificador opaco e redireciona para `/DemaisSetores/Continuar/{ticket}`. A página de entrada envia `Referrer-Policy: no-referrer` e `Cache-Control: private, no-store`. A query não é registrada pelo aplicativo. Isso não apaga logs que o proxy ou o servidor já tenham gravado. URL com CPF pode ficar no histórico do navegador. Quando o parceiro tiver backend, use a chamada de servidor.

Em página própria, a continuação vai para o login com retorno somente em `/DemaisSetores/Continuar/{ticket}`. CPF e atendimento não voltam na ReturnUrl. Uma entrada por iframe sem autenticação mostra recuperação com abertura manual em nova janela; não tenta executar login dentro do quadro. Depois do login valem a permissão do usuário e a restrição da integração. O link temporário resultante é `/DemaisSetores/Abrir/{codigo}`.

Exemplo sintético de incorporação contextual:

`/DemaisSetores/Integrar?integracao=parceiro-exemplo&tipoIdentificador=ID&paciente=000120&codigoAtendimento=000088&nomeDocumento=Prescri%C3%A7%C3%A3o%20M%C3%A9dica&modo=documento&destino=iframe&origemIncorporacao=https%3A%2F%2Fparceiro.exemplo`

A origem é validada na criação do ticket, relida na continuação e na emissão transacional, vinculada ao código de iframe e revalidada no consumo e nas consultas. O código de página de uma autorização por usuário usa origem vazia. Nenhuma etapa escolhe a primeira origem cadastrada ou usa `Referer` como autorização.

## Autenticação

A chamada de servidor continua disponível.

- `POST /api/integracoes/demais-setores/aberturas`
- Cabeçalho `X-InovaGed-Integracao`: identificador cadastrado
- Cabeçalho `X-InovaGed-Chave`: segredo cadastrado

O segredo não deve ser colocado no JavaScript do parceiro. Cada integração tem tenant, sistema, unidade, origens, tipos, prazo máximo e situação. Sem tipo informado, a consulta do atendimento só abre se essa opção estiver marcada no cadastro.

## Corpo

```json
{
  "paciente": { "tipoIdentificador": "ID", "identificador": "000120" },
  "sistemaOrigem": "SISTEMA",
  "unidadeOrigem": "UNIDADE",
  "codigoAtendimento": "00088",
  "codigoDocumento": "DS23",
  "nomeDocumento": "Prescrição Médica",
  "modo": "consulta",
  "origemIncorporacao": "https://sistema-parceiro.exemplo"
}
```

`ID` e `CPF` não são intercambiáveis. O CPF é normalizado e validado só no formato. Zeros à esquerda do ID são preservados. CPF, chave e segredo não vão para a URL.

## Resposta

```json
{
  "codigo": "abertura_concedida",
  "mensagem": "Abertura concedida para o atendimento informado.",
  "urlAbertura": "/DemaisSetores/Abrir/<codigo-opaco>",
  "urlIncorporacao": "/DemaisSetores/Abrir/<outro-codigo-opaco>",
  "validade": "2026-10-13T15:00:00Z",
  "escopo": "tipo",
  "correlacao": "<correlacao>",
  "ocorrencias": 1
}
```

Cada código é de uso único. A troca do código pela sessão ocorre numa única transação: o código só fica consumido se a sessão for criada e associada. Se a criação falhar ou for cancelada antes da confirmação, o código continua utilizável até expirar. Desativar a integração inutiliza códigos ainda não usados e revoga as sessões. Reativar a integração não recupera essas aberturas.

A página e o quadro recebem códigos diferentes. Cada um vira uma sessão com identificador público não secreto (`/DemaisSetores/ConsultaIntegrada/{publicId}`) e um cookie próprio `ds_ctx_{publicId}`. O cookie sozinho, ou o identificador sozinho, não autoriza a consulta. Várias aberturas na mesma página não compartilham um cookie único.

Em HTTPS o cookie da abertura é `SameSite=None; Secure` e `Partitioned`. Em HTTP local ele é `SameSite=Lax`. `SameSite=None` não garante o funcionamento quando o navegador bloqueia cookies de terceiros. Nesse caso aparece a tela de recuperação, com “Abrir em nova janela”. Ela usa a referência opaca do contexto para criar uma entrada de página própria, exige login e reautoriza o usuário pelo fluxo canônico. O código consumido não é reutilizado como credencial. Um código vencido ou consumido ainda retido também pode localizar o contexto para essa nova autorização, sem expor paciente/documento antes do login. Integração/origem revogada ou contexto eliminado não se recupera. Não há redirecionamento automático em ciclo.

A sessão é somente leitura. Conferência, correção e revisão continuam na consulta autenticada, com permissão explícita. Comandos da sessão, como pesquisa e troca de atendimento, exigem o token entregue no fragmento da primeira abertura, a origem da concessão ou da própria aplicação, e o identificador daquela sessão. O cookie global de autenticação permanece `SameSite=Lax`. O `Referer` não autentica. `frame-ancestors` usa somente a origem concedida.

A lista de tipos da integração é relida a cada requisição da sessão. Lista vazia significa todos os tipos ativos daquele tenant. Lista preenchida restringe listagem, arquivo, texto, campos, achados, checklist e pesquisa a esses códigos. Documento ou vínculo inativo sai do escopo. A mesma regra vale para acesso direto pelo identificador da versão. Remover a origem cadastrada invalida a próxima leitura e a troca de atendimento. Alterar os tipos não exige nova sessão para passar a valer.

Essa convenção de lista vazia vale somente para a configuração da integração. O resultado efetivo da autorização sempre contém os códigos ativos permitidos, já intersectados com a concessão original. `SectorCatalogAccess` distingue `AllActive`, `Restricted` e `None`; em consultas, um array vazio nega todos os documentos. Se uma concessão limitada a DS23 passar a intersectar uma integração limitada a DS08, a sessão é recusada, inclusive para trocar atendimento. A continuação resolve os tipos visíveis pelo serviço no servidor, independentemente do booleano administrativo recebido pelo chamador.

A emissão relê a integração sob bloqueio transacional. Se situação, credencial, origem, tipos, sistema, unidade, prazo ou opções de escopo mudaram desde a resolução do contexto, os dois códigos deixam de ser emitidos e a abertura precisa ser reiniciada. O vínculo de alias também é transacional: exige paciente canônico confirmado na mesma origem, unidade e tenant; conflito ou cadeia de identidades provoca erro e rollback.

A abertura por login preserva `authorizing_user_id` no código e na sessão. A cada leitura o servidor revalida usuário ativo/desbloqueado, tenant, entrada no módulo, tipos visíveis e autorização documental canônica (ABAC e ACL). O filtro compartilhado exige a lista de documentos autorizados para sessões de usuário; uma lista não resolvida ou vazia nega conteúdo. A concessão emitida por backend autenticado mantém seu contrato de integração, sem assumir a identidade de um usuário.

A sessão por login exige também o cookie de autenticação do mesmo usuário e tenant. Logout impede seu uso; um novo login desse mesmo usuário pode retomá-la dentro da validade, sujeito à revalidação. Revogar usuário, módulo, tipos ou acesso documental vale na próxima requisição. A continuação padrão abre em página própria, com origem vazia e `frame-ancestors 'none'`; a continuação explicitamente destinada ao iframe usa a origem autorizada. O recurso PDF permite `'self'` e, quando incorporado, somente a origem parceira concedida, permitindo o visualizador aninhado sem curinga. A incorporação pela API continua vinculada à origem explicitamente validada.

A migration incremental `2026_10_29_demais_setores_user_session.sql` acrescenta a identidade autorizadora. Na primeira execução invalida os códigos e sessões temporários anteriores, incluindo os de backend, pois as antigas concessões administrativas não têm proveniência distinguível. Preserva o histórico e não revoga novas aberturas em reaplicações. Reabra os contextos pelo fluxo autorizado depois do upgrade.

Os testes PostgreSQL e de controller não substituem a homologação de login e cookies em navegador de outra origem. O registro histórico da primeira etapa está abaixo; o contrato atual de recuperação e o roteiro do parceiro estão no final deste documento.

## Registro histórico da primeira etapa em 15/10/2026

Esta seção registra o estado e as pendências daquela etapa. A implementação continuada descrita ao final substitui as pendências já resolvidas; os resultados antigos permanecem como evidência do baseline.

Baseline local: `d0d1a152ba425199ecaf4b6ba39f709d5f285f9b`, sem commits posteriores nem alterações locais no início. Não houve push, publicação ou migration em produção. PostgreSQL 16 descartável em Docker, porta local 55439, foi usado para a validação. Datas dos nomes de migrations seguem a sequência existente no repositório e não indicam a data desta execução.

### Correções e causas

- A autorização da sessão por login selecionava todos os documentos do paciente antes de aplicar ABAC/ACL. Agora os candidatos respeitam atendimento, tipo, documento e versão. A ampliação é explícita na pesquisa e exige também escopo de atendimento e permissão atual para trocar atendimento. Sem cache entre requisições; revogações continuam sendo consultadas no banco.
- A pesquisa integrada valida sessão e token em uma operação do serviço, sem repetir a consulta documental de autorização para validar o comando. A troca de atendimento mantém revalidação antes de alterar o contexto.
- `UserIsActiveAsync` mantém `is_active`, `is_locked` e `reg_status`, acrescentando `deleted_at_utc is null`. A suspeita inicial sobre incompatibilidade de `reg_status` não se confirmou: a migration canônica `2026_08_21_administration_legacy_schema_compat` acrescenta a coluna ao schema base. O problema confirmado era a exclusão lógica não considerada. O teste novo usa a aplicação completa instalada e atualizada pelo migrador oficial.
- Parâmetros de query repetidos, inclusive valores iguais e variações de maiúsculas, são recusados antes de criar ticket. O contrato também recusa chaves repetidas por capitalização, caracteres de controle em integração/atendimento e GUID vazio. O serviço valida novamente o rascunho antes de persistir.
- Origens precisam conter apenas esquema, host e porta, com barra final opcional. Caminhos, query, fragmento, credenciais e curinga são recusados. A troca de código revalida a origem atual, além da revalidação já existente nas leituras. Isto não implementa o parâmetro de origem na URL contextual: essa parte permanece pendente.
- Middlewares de auditoria e incidentes agora registram o modelo da rota de Demais setores, sem valor do código ou ticket, sem query e sem parâmetros clínicos de rota. Erros dessas rotas conservam tipo e correlação, sem mensagem/stack que possam conter entrada clínica. Testes exercitam respostas 403 e exceções com valores sintéticos sensíveis. Isso não substitui a configuração de logs do proxy/servidor HTTP.

### Política de contextos temporários

Configuração `DemaisSetores:TemporaryContexts`, igual em todos os hosts:

```json
{
  "DemaisSetores": {
    "TemporaryContexts": {
      "TicketMinutes": 15,
      "TicketsPerMinute": 30,
      "MaxPendingTickets": 200,
      "MaxRetainedTickets": 2000,
      "RetentionHours": 24,
      "BatchSize": 500,
      "CleanupSeconds": 60
    }
  }
}
```

O limite de admissão é por integração, serializado pelo bloqueio transacional do cadastro. Conta tickets criados no último minuto, pendentes ainda válidos e total retido; o teto total impede crescimento sem limite quando a limpeza falhar. A recusa externa continua genérica. A expiração é aplicada ao usar o ticket, independentemente do worker. O prazo e a retenção são assuntos distintos: reter um ticket expirado não autoriza utilizá-lo.

O worker começa após o primeiro intervalo, usa lotes limitados por tabela e `FOR UPDATE SKIP LOCKED`. Cada exclusão é uma única operação transacional. Cancelamento ou falha permite repetir o próximo ciclo; lotes já confirmados não precisam ser refeitos. Em vários hosts, os bloqueios impedem disputa pelas mesmas linhas. Não exige cache distribuído. Validação de configuração impede políticas inválidas no startup.

Tickets, códigos e sessões são eliminados somente após expiração mais retenção. Solicitações de abertura são eliminadas após criação mais retenção; o mínimo configurável de duas horas supera a validade máxima de uma concessão. A detecção atual de idempotência fica limitada a essa retenção e ainda não recupera resposta perdida. Dados canônicos de paciente, atendimento, documento, versão, auditoria e classificação não são eliminados por esta rotina. Esta política não altera a temporalidade arquivística do GED.

Meter `InovaGed.Sectors.TemporaryContexts`: `sector.tickets.created`, `sector.tickets.rejected`, `sector.tickets.quantity`, `sector.contexts.expired_deleted` e `sector.contexts.cleanup_failures`. A quantidade é uma amostra por ciclo; a exclusão é contada por tabela. Nenhuma métrica tem CPF, paciente, query, código opaco ou segredo. O log de admissão deixou de emitir a referência do ticket. A observação destas métricas depende da configuração do coletor/exportador do host.

Migration incremental: `2026_10_30_demais_setores_context_lifecycle.sql`. Acrescenta índices de integração/criação, pendências e expiração. Manifesto, lista obrigatória e script consolidado foram atualizados. Nenhuma migration publicada foi editada.

### Verificações executadas e evidências

Os arquivos de execução ficam em `artifacts/sector-validation/` (ignorados pelo Git):

| Verificação | Classificação | Evidência/limite |
| --- | --- | --- |
| Build da solução | Executado com sucesso | `build-final.log`; avisos existentes de compilação e dependências continuam presentes. |
| Testes iniciais de Demais setores | Executado com sucesso | `baseline-sectors.trx`: 19 aprovados, zero ignorados. |
| Instalação canônica inicial | Executado com sucesso | `install.log`: base completa e manifesto pelo migrador com `install --verify`. |
| Instalação limpa e upgrade com dados de usuário | Executado com sucesso | `Canonical_install_and_upgrade_preserve_identity_and_revalidate_user_state`: instalações separadas, upgrade da referência, repetição de `apply --verify`, identidade preservada, abertura e revalidação de ativo/bloqueado/inativo/excluído. Logs em `upgrade-*/`. O catálogo e a entrada do usuário nesses testes são controlados por fixture; não equivalem a login real. |
| Autorização, escopo, concorrência, tickets, auditoria e observabilidade | Executado com sucesso | `sectors-final.trx`: 80 aprovados, zero falhas, zero ignorados. Testes PostgreSQL do módulo cobrem ACL, tipos sem interseção, revogação, duas origens aceitas e terceira negada, usuário, troca de identidade no controller, mudança concorrente da integração, conflito de identificadores, admissão concorrente, teto retido, cancelamento, expiração e lotes repetíveis. |
| Suíte ampla Application.Tests | Falhou | `application.trx`: 701 aprovados, 27 falharam, 42 ignorados; `baseline-application.trx`: 689 aprovados, as mesmas 27 falhas e 42 ignorados. Comparação por nome sem diferenças no conjunto de falhas. Os 42 ignorados não constituem validação de PostgreSQL. |
| Planos de consulta reais | Executado com sucesso | `query-plans.log`, reproduzível por `scripts/homologation/sector-query-plans.sql` em banco descartável. |
| Primeiro ensaio HTTP | Falhou | `web.log`: seed registrou FK de tenant ausente; acesso por `127.0.0.1` retornou 400, pois esse host não está no `AllowedHosts` versionado. |
| Startup HTTP e URL → continuação → login | Executado com sucesso | Novo ensaio por `localhost`, com seed automático desabilitado somente no processo de teste: login 200, entrada 302 para referência opaca, continuação 302 para login sem paciente/atendimento na ReturnUrl. `web-localhost.log` e `url-login-result.json`. Não foi validado login → documento em navegador. |
| Componentes Chromium existentes | Executado com sucesso | `browser-components.trx`: três aprovados, zero ignorados. Usam respostas simuladas e não substituem integração entre aplicações ou revisão visual das páginas reais. |
| Homologação em duas origens, cookies bloqueados e recuperação | Não executado | O gate não foi aprovado; recuperação funcional e resposta perdida ainda exigem implementação. |
| Revisão visual e zoom de 200% | Não executado | Nenhum CSS foi alterado e nenhuma evidência visual de aprovação foi produzida. |
| Nova evolução dependente de upload/OCR/IA e conferência | Bloqueado | Depende do gate de integração exigido nesta solicitação. Os serviços existentes foram inspecionados, mas a jornada completa não foi homologada. |

O teste de planos usa somente dados sintéticos e desfaz a fixture por rollback: um paciente, mil atendimentos, cem mil documentos. Na última amostra, a consulta anterior devolveu cem mil candidatos em 65,554 ms; a restrição ao atendimento devolveu cem. O predicado equivalente `(reg_status='A' or reg_status is null)` evitou a subestimativa de seletividade de `coalesce`, permitindo `ix_sector_link_encounter` e `document_pkey`: 1,047 ms, 503 buffers. Um índice parcial candidato foi medido e descartado por não melhorar o plano; não foi acrescentado ao schema. Estes números medem candidatos, não latência total, ABAC, PDF ou concorrência de produção.

As falhas amplas existentes incluem DI parcial sem `IGedAccessPolicyService`/`DocumentAppService`, contratos de etiquetas/UI/protocolo/IA e testes de arquivo físico que não usam o DSN descartável. A lista exata está em `existing-failures.txt`. Não foram enfraquecidos testes para obter aprovação.

### Gate e pendências prioritárias

O gate permanece **não aprovado**. Nenhum dos seis cenários ponta a ponta foi declarado homologado: URL → login → documento; API → iframe em outra origem; origem incorreta no navegador; recuperação em nova janela; resposta perdida; revogação em todos os recursos no navegador. Negação de origem, ACL e revogação no servidor possuem testes comportamentais, mas isso não substitui esses cenários reais.

1. Completar origem explícita na URL contextual, parser, ticket, gerador e testes em conjunto; preservar a abertura por login em página própria.
2. Implementar recuperação de iframe por referência opaca com nova autorização, sem reutilizar código consumido, e estados não clínicos para o parceiro.
3. Completar fingerprint de idempotência, conflito de conteúdo, consulta autenticada de estado e política limitada de recuperação da concessão.
4. Completar assistente administrativo por integração/origem/tipo e diagnóstico por correlação. O assistente existente continua sendo gerador de modelo, não concessão permanente.
5. Preparar tenant/usuários/documentos sintéticos e parceiro em origem separada, configurar seed/hosts conforme o ambiente e executar todo o gate até o documento, com login real.
6. Depois do gate, concluir e testar upload interrompido/retomado, falha entre armazenamento e vínculo, processamento por etapa, extração governada com evidência e preservação de confirmação humana; concluir conferência sem inferir administração/realização/cobrança por mera prescrição ou menção.
7. Validar páginas reais em 360, 768, 1366 e 1920 pixels e zoom de 200%, com capturas e revisão visual. Homologar dois iframes, duas abas, logout, revogação e cookies bloqueados.

O contrato do fornecedor clínico continua sendo uma dependência externa sem cliente MV/Soul homologado. Isso não é justificativa para acesso anônimo nem substitui a homologação com parceiro sintético.

Arquivos de implementação alterados: `SectorOpeningService`, `SectorOpeningExchange`, `ISectorOpeningService`, `SectorEntryContract`, `DemaisSetoresController`, composição de dependências e middlewares `RequestAudit`, `Audit`, `AccessDeniedAudit`, `IncidentCapture`. Novos: `SectorOrigin`, `SectorTemporaryContextMaintenance`/worker/options, `SectorRequestRedaction`, testes `SectorRequestAuditTests` e script `sector-query-plans.sql`. O teste `SectorEntryAndOpeningGateTests` foi ampliado. Além da migration incremental, foram atualizados os dois manifestos e o script consolidado. Não houve mudança em dados de produção, catálogo canônico, versões documentais, classificação ou temporalidade.

O escopo fica fixo na abertura:

- `documento`: documento e versão permanecem os da concessão.
- `tipo`: um tipo dentro do atendimento. Não troca o episódio.
- `atendimento`: o episódio concedido. Navegar para outro atendimento do mesmo paciente exige a opção explícita na integração e na abertura. Pertencer ao mesmo paciente não autoriza a troca.

```html
<iframe src="https://inovaged.exemplo/DemaisSetores/Abrir/<codigo>" title="Documentos do atendimento" width="100%" height="800"></iframe>
```

Uma concessão de documento não lista o prontuário inteiro. Várias ocorrências do mesmo tipo são listadas com data, origem e versão. Nenhuma é escolhida sozinha. Documento ausente mantém o atendimento e não mostra outro episódio.

## Origem, prazo e revogação

A origem do corpo precisa estar cadastrada na integração. O `frame-ancestors` usa essa origem, não um curinga. O prazo segue o cadastro, no máximo 60 minutos. Uma nova gravação da integração substitui a chave. Códigos vencidos ou já usados respondem que a abertura expirou.

## Erros estáveis

| Código | Quando |
| --- | --- |
| `integracao_nao_autorizada` | Credencial ausente, inativa ou divergente. Não indica se o prontuário existe. |
| `solicitacao_invalida` | CPF inválido, atendimento vazio, tipo fora do catálogo ou sistema divergente. |
| `origem_indisponivel` | A origem clínica configurada não respondeu. |
| `paciente_nao_encontrado` | Integração válida e paciente não localizado na origem e unidade. |
| `atendimento_divergente` | O atendimento não pertence ao paciente. |
| `documento_ausente` | Não há documento elegível. O contexto pode ser aberto. |
| `varias_ocorrencias` | Há mais de um documento e nenhum foi escolhido. |
| `acesso_negado` | Origem ou tipo fora do cadastro da integração. |
| `tipo_ambiguo` | O nome corresponde a mais de um tipo ativo. Nenhum é escolhido. |
| `tipo_contraditorio` | Código e nome apontam para tipos diferentes. |
| `abertura_repetida` | HTTP 202: mesma chave e fingerprint identificam a mesma solicitação; consulta de estado e recuperação autenticada disponíveis. |
| `idempotencia_conflitante` | HTTP 409: chave já pertence a outro conteúdo ou solicitação legada sem fingerprint. |
| `limite_excedido` | Mais de 30 aberturas da integração no último minuto. |

A página e o quadro são gravados na mesma transação, com a mesma correlação. Se a auditoria falhar depois da gravação, a abertura continua concedida e o erro fica no log. `documentoId` ou `versaoId` mantêm o escopo no documento encontrado. Eles não abrem o atendimento inteiro. Vários documentos do mesmo tipo geram `varias_ocorrencias` e a lista para seleção. Vários tipos com o mesmo nome geram `tipo_ambiguo`.

Identidade e atendimento já confirmados na origem podem ser abertos sem uma nova consulta clínica. Sem essa confirmação, a origem precisa responder. Indisponibilidade não cria paciente nem atribui atendimento só pelo parâmetro recebido. ID e CPF só apontam para a mesma identidade quando a origem devolve os dois identificadores. Nome parecido não une prontuários. Não há cliente MV/Soul homologado.

O iframe sem cookie mostra a tela de recuperação e oferece uma nova janela por referência opaca. O código já consumido não é reutilizado para conceder acesso. O quadro usa o layout sem menu administrativo. A consulta incorporada permanece somente leitura.

## Sessão e mensagens

O parceiro pode enviar `postMessage` com `tipo: "ds-atendimento"` e `codigoAtendimento`. O quadro só aceita `event.origin` igual à origem da concessão, com esquema `http` ou `https`. O único comando que altera contexto é esse. O servidor reautoriza a sessão, o escopo e o token. A mensagem de carregamento informa apenas que a página abriu. O quadro não envia texto clínico, trecho, nem o PDF para a página pai.

A recuperação e o acompanhamento da sessão enviam somente `{tipo:"ds-estado",estado:"autorizacao-necessaria"}` ou estado `expirada`, para a origem concedida. O componente verifica a validade local e consulta um estado sem dados clínicos a cada 30 segundos e ao recuperar foco. A revogação continua sendo aplicada pelo servidor em cada recurso, sem depender desse acompanhamento visual.

A consulta incorporada lê documentos, PDF com intervalo, texto, pesquisa, campos, achados e checklist já gravados, no escopo da abertura. Ela não usa a sessão de login do GED e não amplia o acesso para preencher a tela. A pesquisa é literal, com sinônimos cadastrados no tipo. Análise semântica não é executada. Outros episódios só entram na pesquisa quando a navegação entre atendimentos está permitida.

## Origem clínica

Não há cliente homologado do MV/Soul. Testes com respostas HTTP simuladas não homologam essa integração. A configuração é global (`ClinicalSource`), não por tenant. O usuário não escolhe o host.

Contrato necessário, ainda não confirmado com o fornecedor:

| Uso | Caminho | Autenticação | Resposta esperada |
| --- | --- | --- | --- |
| Paciente e atendimentos | `GET {BaseUrl}{PatientPath}?codigo&tipoIdentificador&sistema&unidade` | cabeçalho `X-InovaGed-Origem`, valor da variável `INOVAGED_CLINICAL_SOURCE_KEY` | `status` `encontrado` ou outro valor; `codigo`; `nome`; `atendimentos[]` com `codigo`, `tipoExterno`, `entrada`, `alta`, `situacao` |
| Metadados | `GET {BaseUrl}{DocumentsPath}` | o mesmo cabeçalho | `documentos[]` com `id`, `nome`, `situacao`, `atualizadoEm` |
| Conteúdo | `GET {BaseUrl}{ContentPath}?documentoExterno` | o mesmo cabeçalho | bytes do arquivo e tipo de conteúdo |

Os caminhos precisam ser relativos ao `BaseUrl`. Destino absoluto, outro host ou redirecionamento é recusado e a credencial não é enviada adiante. Tempo limite: 15 segundos. Falha de rede, tempo ou JSON inválido responde indisponível e não cria paciente. Sem `Enabled`, `BaseUrl` e `PatientPath`, a consulta responde indisponível e informa que nenhum paciente foi simulado.

Unidade e sistema da abertura precisam coincidir com o cadastro da integração. O identificador do paciente e o código do atendimento são os da origem. Estados de documento da origem permanecem metadados até um contrato real definir o significado de cada situação.

## Continuação — integração e recuperação

### Alterações de banco e autorização

As migrations desta continuação são `2026_10_31_demais_setores_recovery.sql` e `2026_11_01_demais_setores_ticket_binding.sql`, presentes nos manifestos e no consolidado. Acrescentam origem/destino, fingerprint e contador de recuperação, nome de exibição e índice de diagnóstico por tenant/correlação. Nenhum script publicado foi editado.

O ticket vincula tenant, integração, sistema/unidade, hash da credencial, origens, tipos e opções/prazo existentes na criação. Mudança dessa configuração antes do login é recusada, evitando reinterpretar o ID em outra origem clínica. Recuperação compara a configuração novamente sob o bloqueio transacional da criação do novo ticket. Nome de exibição não muda escopo. Tickets legados sem fingerprint exigem nova entrada após upgrade; dados e sessões existentes não são reescritos.

### Resposta perdida e idempotência

Cabeçalhos de todas as operações: `X-InovaGed-Integracao`, `X-InovaGed-Chave` e `X-InovaGed-Idempotencia`. Segredo permanente somente no backend. A chave de solicitação aceita até 80 caracteres. O fingerprint considera integração, paciente/tipo, sistema/unidade, atendimento, nome/código documental, documento/versão, modo e origem; CPF é normalizado, zeros do ID são mantidos e campos textuais são aparados.

| Operação | Resultado |
| --- | --- |
| `POST /api/integracoes/demais-setores/aberturas` | Primeira emissão: 200 e dois códigos distintos. Mesma chave/conteúdo: 202, mesma correlação e nenhum código novo. Conteúdo diferente ou solicitação legada sem fingerprint: 409. |
| `GET /api/integracoes/demais-setores/aberturas/estado` | Estado, correlação, validade e contador, sem paciente, documento ou conteúdo clínico. |
| `POST /api/integracoes/demais-setores/aberturas/recuperar` | Reautentica, revalida contexto e emite substitutos, invalidando códigos e sessões anteriores daquela solicitação. |

Estados: `disponivel`, `utilizada`, `expirada`, `revogada`, `legada` ou `ausente`. Limite de três recuperações por solicitação; esgotamento responde 429. A validade original não é ampliada. Depois do vencimento, o backend precisa de outra solicitação autorizada. Recuperações concorrentes usam o mesmo bloqueio da emissão. Metadados seguem a retenção configurável; não há recuperação ilimitada.

O banco guarda somente hashes dos códigos, fingerprint e contador. Não persiste os códigos da primeira resposta em texto aberto. Recuperar significa emitir substitutos limitados, não reutilizar código consumido. Respostas usam `private, no-store`.

### Recuperação de navegador e administração

Sem cookie ou após expiração, a página oferece “Abrir em nova janela”. `/DemaisSetores/Recuperar/{referencia}` cria contexto de página própria, passa por login e autorização canônica. A referência sozinha não concede conteúdo. Código vencido/consumido ainda retido pode localizar contexto para nova autorização, sem voltar a ser credencial. Integração/origem revogada ou referência fora da retenção não se recupera. Não há ciclo automático de redirects nem relaxamento do cookie global.

O assistente seleciona integração por nome, exige origem explícita e tipo do catálogo, gera modelo contextual, iframe e chamada backend. Distingue modelo, concessão e sessão. O diagnóstico por correlação é POST com antiforgery e permissão de parametrização, restringe o tenant e retorna somente contagens, validade e recuperação. Não mostra segredo, token ou conteúdo clínico.

Não há middleware OIDC/SAML configurado no código inspecionado. O login canônico continua sendo a fronteira do usuário. Certificado não equivale a federação do parceiro. SSO depende de contrato real de IdP, validação de emissor/audiência e mapeamento para `app_user`/tenant/permissões; os três parâmetros clínicos nunca autenticam usuário.

### Defeitos expostos pelo ensaio real

- Três consultas enviavam `{SectorSessionScope.LinkFilter}` literalmente ao PostgreSQL: corrigida a interpolação em documentos, campos e achados. O teste canônico agora executa a tela e os painéis.
- Dapper recebia `DateTime` onde os construtores exigiam `DateOnly`/`DateTimeOffset`: conversões explícitas preservam data clínica e instantes UTC, sem regra arquivística paralela.
- PDF aninhado precisava de `'self'` e da origem parceira concedida em `frame-ancestors`. A página própria continua sem incorporação externa e sem curinga.
- Os controles móveis estavam fora do elemento pesquisado pelo JavaScript. Agora funcionam por clique, setas, Home e End, com roles e seleção acessíveis.
- Capturas reais revelaram contraste inadequado no cartão documental e fonte padrão na incorporação. Corrigidos esses pontos, dimensionamento e desabilitados legíveis. Isso não representa aprovação visual integral do módulo.

### Executar o parceiro sintético

Use somente o PostgreSQL descartável `inovaged-sector-validation`, banco `sector_clean`, porta 55439. Banco vazio: migrador `install --verify`; banco descartável existente: `apply --verify`. Não importe a base sobre dados existentes.

```powershell
$env:ConnectionStrings__DefaultConnection='Host=127.0.0.1;Port=55439;Database=sector_clean;Username=postgres;Password=inovaged-synthetic-validation'
dotnet run --project InovaGed.Database.Migrator -- apply --verify
python scripts/homologation/seed-sector-partner-fixture.py
```

O seed restringe container/banco, recusa tenant `default` diferente da fixture e cria somente dados sintéticos. Conta: `sector@example.test`, senha de teste `Sector-Test-2026!`. Não use a fixture em instalação real. Para iniciar o GED nesse processo:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='https://localhost:7189'
$env:SystemSeed__Enabled='false'
$env:OcrWorker__Enabled='false'
$env:PacsIntegration__Enabled='false'
$env:Storage__Local__RootPath=Join-Path (Get-Location) 'artifacts/sector-validation/storage'
dotnet run --no-build --no-launch-profile --project InovaGed.Web
```

Em outro processo, o parceiro serve somente loopback, sem CORS nem logs de URL/corpo:

```powershell
$env:INOVAGED_PARTNER_HOST='https://localhost:7189'
$env:INOVAGED_PARTNER_CLIENT='partner-gate'
$env:INOVAGED_PARTNER_SECRET='synthetic-partner-secret-2026'
$env:INOVAGED_PARTNER_INSECURE_TLS='1'
python tools/parceiro-demais-setores/server.py
```

O último sinalizador serve apenas para o certificado de desenvolvimento local. Não altera TLS/cookies do GED. Acesse `http://127.0.0.1:5190` ou `http://localhost:5190`; uma terceira origem é recusada. O segredo não aparece no HTML/JavaScript. Há abertura backend, consulta de estado, recuperação e dois iframes com mensagens de estado sem conteúdo clínico.

```powershell
$env:INOVAGED_SECTOR_GATE='1'
dotnet test InovaGed.UiTests --no-build --filter FullyQualifiedName~SectorPartnerGateBrowserTests
```

O teste expira somente uma sessão do tenant fixo no container/banco descartáveis. Usa Chromium completo e restrição real de terceiros via `Network.setCookieControls`, conforme o [protocolo do Chromium](https://chromedevtools.github.io/devtools-protocol/tot/Network/#method-setCookieControls). Cookie particionado é exercitado na API/iframe; cookie global Lax permanece recusado no login incorporado, com recuperação em janela própria.

### Resultados e limites da continuação

Build aprovado. `recovery-focused.trx`: 87 aprovados, zero falhas/ignorados, incluindo instalação limpa, upgrade, tela integrada, escopo, recuperação, configuração do ticket, diagnóstico protegido, observabilidade e desativação seguida de reativação. `partner-gate.trx`: 4 aprovados, zero falhas/ignorados; o ensaio integrado registra onze etapas concluídas em `artifacts/sector-validation/recovery-ui/gate-results.json`. Inclui duas origens, dois iframes, cookies de terceiros restritos, resposta perdida, recuperação, logout e revogação. `recovery-application.trx`: 710 aprovados, as mesmas 27 falhas por nome do baseline e 42 ignorados. A suíte ampla precede o teste adicional do diagnóstico e a correção final de revogação persistente; ignorados não constituem homologação PostgreSQL.

A migration incremental `2026_11_02_demais_setores_recovery_revocation.sql` acrescenta `revoked_at` aos tickets e solicitações, preservando fingerprints e histórico. Desativar a integração revoga esses contextos na mesma transação das sessões/códigos. Reativá-la não restaura continuação, repetição ou recuperação anterior; exige nova abertura autorizada e nova chave. A migração também marca os contextos de integrações já inativas. Manifestos e consolidado incluem a alteração; a instalação limpa e o upgrade foram repetidos com ela.

Capturas em `artifacts/sector-validation/recovery-ui/` abrangem 360/768/1366/1920 pixels e simulação CSS de 200%. Isso não equivale a zoom nativo. O PDF foi validado como arquivo de uma página e seu acesso HTTP foi testado; capturas do renderizador nativo headless tiveram áreas vazias/escuras. Não há aprovação visual integral da pintura do PDF nem dos fluxos de Cadastrar/OCR por essas imagens.

Continuam pendentes: automação durável upload → prévia/OCR → extração por versão/etapa; extração governada de itens com evidência e revisão preservada; conferência completa; interrupção/retomada e falha entre armazenamento/vínculo; fornecedor clínico/IdP reais; zoom nativo de 200% e validação visual do PDF em navegador de uso real. Aceite da integração sintética e aceite do pipeline clínico são separados. Nenhum teste daqui homologa automaticamente OCR/IA ou gera cobrança.

## Continuação: processamento por versão

O item de automação durável acima foi avançado com a migration `2026_11_03_demais_setores_processing.sql`. Ela reutiliza `ged.processing_job`, adicionando o tipo `SECTOR_EXTRACT` e um registro de coordenação por tenant/versão. O vínculo confirmado agenda a primeira execução na mesma transação. Mudanças em texto nativo, texto de OCR, índice de texto, situação de OCR e prévia agendam nova verificação. Vínculos existentes também são agendados no upgrade. A fila não contém texto clínico.

`GedProcessingWorker` entrega esse tipo a `SectorVersionProcessor`. A leitura usa a versão exata do vínculo e distingue `TEXTO_NATIVO` de `OCR`. O mecanismo atual é `REGRAS_LOCAIS_V1`, com as regras locais já existentes: não é IA nem extração estruturada completa de dose/via/frequência. Trecho, página identificada, documento/versão e contexto clínico permanecem rastreáveis. Não há confiança inventada, confirmação automática ou cobrança.

As regras locais também passaram a priorizar negações explícitas como “não foi administrado”, “não realizado” e “sem administração”, em vez de sugerirem administração ou realização a partir dessas palavras. Isso não constitui interpretação clínica geral: negações complexas, contexto longitudinal e ambiguidade ainda exigem revisão humana.

OCR, prévia e extração têm estados separados. Sem texto, a extração registra espera, ausência de texto utilizável ou bloqueio por falha de OCR; não declara conclusão por sucesso de upload. Uma nova persistência de texto aciona outra execução. Abrir a consulta continua sem processamento pesado. O POST de leitura apenas agenda a fila e retorna HTTP 202. A consulta mostra rótulos legíveis para essas situações.

A coordenação serializa notificações e conclusão do job na mesma transação da gravação das sugestões. Um texto alterado enquanto a extração está em andamento pode agendar a próxima execução após o commit, sem perder a notificação. O hash de entrada evita recriar sugestões quando só a situação de prévia muda. Reprocessamento remove somente sugestões locais sem revisão/histórico; decisões humanas e seu histórico são preservados. Achados abertos substituídos ficam `SUPERADO`, mantendo a evidência histórica. A gravação de revisão de campo usa transação e bloqueio da linha para concorrer com a extração.

Queda do worker deixa o trabalho recuperável pela política de lease de dez minutos da fila. Cancelamento normal devolve esse tipo à fila. Falhas seguem o backoff e o limite de tentativas existentes; o POST permite nova solicitação explícita. O processamento depende de `GedProcessingWorker` em execução; OCR e prévia continuam dependendo dos seus workers/provedores próprios.

Na implantação desta alteração, pause os workers GED antigos antes de aplicar a migration e inicie a versão atual em seguida: o binário antigo não conhece `SECTOR_EXTRACT`. Não misture versões dos consumidores dessa fila durante esse upgrade. Nenhuma alteração foi aplicada em produção nesta execução.

O ambiente desta execução não tem `tesseract` ou `ocrmypdf` disponíveis no PATH. Os ensaios de persistência de resultado OCR são sintéticos e não homologam o motor. Continuam necessários: ensaio completo de upload em partes com interrupção/falha/retomada, execução do OCR real, extração clínica estruturada governada pelos provedores de IA, checklist completo e aceite visual do fluxo. O avanço desta etapa não equivale ao aceite integral do bloco C.

Validação desta etapa: build da solução aprovado; `processing-focused.trx` com 54 aprovados, zero falhas/ignorados; `processing-browser.trx` com quatro aprovados, zero falhas/ignorados. O teste canônico executa instalação limpa e upgrade, espera de texto, texto nativo, resultado de OCR sintético, repetição sem duplicar, preservação de revisão/histórico, recuperação de job abandonado, notificação concorrente e rollback com falha injetada seguido de retomada. O worker hospedado da aplicação também processou a fixture sintética até `COMPLETED`, com sugestão `TEXTO_NATIVO`/`REGRAS_LOCAIS_V1`, conforme `processing-worker-smoke.txt`.

A suíte ampla final (`processing-application.trx`) terminou com 717 aprovados, 27 falhas e 42 ignorados, total 786. Os 27 nomes de testes com falha são exatamente os do baseline salvo; nenhum teste adicional ficou falhando. Ignorados não representam aprovação. O resumo reproduzível fica em `artifacts/sector-validation/processing-results.json`. As capturas e as onze etapas do parceiro sintético foram renovadas; continuam valendo as limitações de pintura de PDF e zoom nativo documentadas acima.

Arquivos principais desta etapa: `InovaGed.Infrastructure/Sectors/SectorVersionProcessor.cs`, `Ged/Documents/GedProcessingWorker.cs`, `Sectors/DemaisSetoresService.cs`, registro em `DependencyInjection.cs`, regras em `InovaGed.Application/Sectors/SectorRules.cs`, controller e view de consulta, testes de regras e `InovaGed.Application.Tests/Sectors/SectorVersionProcessingGate.cs`, além da migration e dos três manifestos/scripts existentes. Nenhuma regra de classificação ou temporalidade arquivística foi criada.

## Continuação: upload interrompido e vínculo recuperável

O início do upload em partes persiste `SectorContext` (paciente, atendimento e tipo documental) em `ged.upload_session.metadata_json`. A conclusão verifica tenant, proprietário e esse contexto antes de chamar a persistência do arquivo. O serviço de vínculo repete a verificação. Uma troca de contexto retorna `CONTEXTO` sem reinterpretar o envio. Não foi necessária nova migration; os metadados existentes continuam compatíveis com uploads genéricos.

Sessões setoriais antigas sem `SectorContext` não recebem um contexto presumido. O endpoint de conclusão recusa essas sessões. Arquivos já armazenados continuam no GED e podem seguir o fluxo explícito, autorizado, de vínculo de documento existente; arquivos ainda não concluídos precisam de um novo envio contextualizado. Não há exclusão de conteúdo ou alteração de histórico nesse tratamento.

A criação do vínculo, ajuste de visibilidade, ACL e agendamento da extração agora compartilham uma transação. O bloqueio do atendimento e do documento serializa conclusões concorrentes. A repetição só retorna sucesso para a mesma versão, paciente, atendimento, catálogo e vínculo ativo. Outro vínculo ou histórico exige a operação canônica de correção; não dispara uma inserção duplicada nem reaproveita silenciosamente outro contexto.

Uma falha depois de `upload_session` registrar `COMPLETED`, antes do vínculo, deixa os IDs do documento e da versão disponíveis na consulta de situação. A nova tentativa reaproveita esses IDs e executa o vínculo sem reenviar partes. Na tela, paciente, atendimento e controles dos arquivos ficam bloqueados durante a tentativa; cada envio retoma seu contexto inicial e somente os arquivos pendentes permanecem na lista. A troca explícita de contexto informa que arquivos gravados continuam no GED. O cancelamento confirmado limpa a referência local para permitir nova tentativa; uma recusa não é apresentada como cancelamento concluído.

O cancelamento no serviço canônico bloqueia a sessão e verifica o proprietário antes de alterar qualquer item do lote. Não cancela estados `COMPLETING`/`COMPLETED`. Isso evita transformar um arquivo já concluído em cancelado e protege itens de lote pertencentes a outro usuário. A conclusão repetida também deixa de informar equivocadamente “não enfileirada” quando o processamento foi solicitado na primeira chamada; orienta acompanhar as etapas persistidas.

Foi corrigido ainda o retorno após login para as páginas exatas `/DemaisSetores/Cadastrar` e `/DemaisSetores/Consultar`. O perfil Hospital antes caía na página padrão, mesmo tendo acesso ao módulo. O retorno não libera um prefixo inteiro; a política de autorização setorial permanece aplicada nas páginas e ações.

`SectorUploadGateBrowserTests` usa login real, requisições multipart reais, armazenamento local e PostgreSQL descartável. Exercita parte inválida, partes pendentes, repetição de parte aceita, retomada, contexto divergente antes da gravação, falha injetada na criação do vínculo depois do armazenamento, duas conclusões simultâneas, uma única versão/vínculo e igualdade SHA-256 entre arquivo enviado e servido. Testa também cancelamento e recusa de novas partes de uma sessão cancelada. O gatilho de falha só opera no tenant fixo da fixture e em nomes sintéticos `SECTOR-UPLOAD-GATE-*`, sendo removido em `finally`. Os testes externos compartilham uma coleção para não alterarem simultaneamente a fixture; os vínculos sintéticos de upload são inativados após o ensaio, preservando arquivos e histórico.

Limites deste ensaio: retomada com a sessão conhecida no mesmo cliente, sem homologação de recuperação automática após fechar/reabrir o navegador; falha controlada após o registro de conclusão do armazenamento, sem simular queda do processo no intervalo anterior entre gravação física e esse registro. OCR real e extração clínica estruturada por IA continuam pendentes, assim como o aceite visual integral. Este teste de upload não aprova automaticamente essas etapas.

Resultados: build da solução aprovado; `upload-focused.trx` com 59 aprovados e zero falhas/ignorados. A suíte ampla final (`upload-application.trx`) passou 722 testes, manteve exatamente os mesmos 27 nomes de falhas do baseline e teve 42 ignorados, total 791. O ensaio conjunto final (`upload-browser.trx`) aprovou cinco testes, sem falhas/ignorados, incluindo os cenários anteriores do parceiro e o novo fluxo de upload. A recusa de cancelar uma sessão já concluída e a manutenção do estado armazenado também foram verificadas. O resultado detalhado fica em `artifacts/sector-validation/upload-gate.json`; contadores e limites, em `upload-results.json`. As onze etapas e capturas do parceiro foram renovadas, sem alterar as ressalvas anteriores de revisão visual.

Arquivos principais desta continuação: DTO de início de upload e `SectorUploadContext` na Application; `UploadChunkService` e `DemaisSetoresService` na Infrastructure; `DemaisSetoresController`, `AppStartRouteResolver` e `demais-setores.js` na Web; `SectorUploadGateBrowserTests`, coleção compartilhada do parceiro e testes do retorno após login. Não houve nova migration, push, merge ou alteração de produção.

## Continuação: itens clínicos estruturados e confirmação durável

A tarefa `ExtractClinicalItems` foi adicionada ao catálogo de IA. Ela é independente de `ExtractMetadata`: habilitar sugestão de título/descrição não habilita extração clínica. A configuração global precisa mapear `DocumentAi:TaskModels:ExtractClinicalItems` para um modelo explicitamente permitido e homologado para JSON estruturado; a política do tenant também precisa autorizar a tarefa, provedor, modelo, cota e tamanho de entrada. A migration `2026_11_04_demais_setores_clinical_extraction.sql` não habilita nenhuma política nem credencial.

Na consulta própria, usuários com revisão de extração e leitura canônica da versão podem abrir “Sugestões clínicas com IA” e iniciar um POST protegido por antiforgery. Não há chamada automática em GET nem envio de todo o prontuário. A fonte é somente o texto da versão exata do vínculo. São suportados DS08, DS10, DS11, DS12, DS20, DS21, DS23 e DS24. As verificações incluem tenant, usuário ativo/não bloqueado/não excluído, permissão setorial, catálogo ativo/visível e ABAC documental. A autorização e a integridade do texto/vínculo são verificadas novamente antes da gravação do resultado.

`SectorClinicalExtractionService` usa o `IDocumentAiGateway` governado já registrado na aplicação. Política, modelo, credencial, reserva de cota, timeout e idempotência continuam na infraestrutura existente. `OnExecutionReserved` permite registrar o identificador da execução na solicitação clínica antes de qualquer envio; falha nesse registro impede o envio e liquida a reserva própria sem consumo remoto. Replays vinculam o mesmo identificador. Nenhuma transação de banco permanece aberta durante a chamada ao provedor; a concorrência da versão é controlada por um advisory lock de sessão, liberado em `finally`.

Cada item estruturado contém categoria, nome, dose, unidade, via, frequência, quantidade, data, contexto e trecho literal, deixando campos não documentados vazios. A saída não pode incluir confiança, decisão humana ou página fornecida pelo modelo. Categoria/contexto são limitados pelo contrato; valores precisam aparecer na evidência e a evidência precisa aparecer literalmente no texto. Quantidade numérica exige indicação explícita de quantidade/qtd/qtde; dose não vira quantidade. Prescrição não aceita contexto de realização, e negação/futuro/planejamento não são tratados como execução. A página é derivada dos marcadores existentes no texto, ficando ausente se o trecho se repete ou não há marcador. Essas verificações não equivalem a interpretação clínica completa: revisão humana continua obrigatória.

A gravação final é transacional e verifica novamente a fonte. Mudança durante a chamada resulta em `DESATUALIZADO`, sem aplicar aquela saída. Sugestões são `SUGERIDO`, mecanismo `IA_CLINICA_V1`, origem `OCR` ou `TEXTO_NATIVO`, com objeto estruturado, versão, evidência, fingerprint da fonte e execução de IA persistidos. Outra solicitação da mesma entrada pode reutilizar o resultado local sem novo envio. A confirmação/correção/rejeição/inconclusão humana da mesma evidência não é substituída por reprocessamento. Revisões exibem autor e data em UTC. Sugestões não revisadas de uma fonte alterada ficam `SUPERADO`; a revisão canônica recusa confirmar uma sugestão superada.

Resultados remotos ainda não resolvidos permanecem `AGUARDANDO_RESULTADO`. A tela permite retomar a mesma solicitação; inclusive após perda da resposta, o registro da execução já existe antes do envio. Uma chave nova não dispara outra chamada enquanto esse resultado estiver pendente. `RemoteOutcomeUnknown` exige reconciliação pela governança existente, sem repetição remota automática. A conclusão local nunca confirma administração, realização ou elegibilidade de faturamento, nem gera cobrança.

O upload passou a confirmar `document_id`/`version_id` em `upload_session` na mesma transação canônica que grava a versão, por métodos de serviço próprios para essa confirmação. Esses identificadores são fornecidos pelo servidor, não por metadados recebidos do navegador. As APIs anteriores de upload permanecem disponíveis. A recuperação de `COMPLETING` com confirmação persistida ocorre antes de ler ou reenviar partes e usa a versão confirmada, mesmo se o documento já tiver outra versão atual. Também repara o agendamento de OCR/prévia que não chegou a ser gravado, sem reenfileirar trabalhos existentes. Sessões legadas sem versão identificável exigem verificação, em vez de presumir a versão atual.

O ensaio de upload foi ampliado para injetar uma falha no registro final `COMPLETED`, depois do commit da versão e de sua confirmação durável, além da falha anterior no vínculo setorial. Ele introduz deliberadamente uma versão posterior e exige recuperar a versão original, sem terceira versão de replay, com uma única solicitação de prévia. Isso testa a retomada entre requisições com estado persistido; não simula encerramento físico do processo. Um arquivo físico escrito antes de um rollback/queda anterior ao commit ainda pode ficar órfão no storage, sem versão documental confirmada; limpeza desses órfãos não foi homologada aqui.

A projeção de processamento também reconhece `ERROR`, estado canônico de falha do OCR, em vez de esperar apenas `FAILED`. Sem texto utilizável, essa situação bloqueia a extração, preservando revisões humanas anteriores. Os testes de estado de OCR continuam sintéticos; isso não homologa o motor de OCR.


Validação desta continuação (15/10/2026): build da solução sem erros, com 13 avisos existentes; 84 testes focados aprovados, nenhum ignorado. A suíte completa de aplicação teve 743 aprovados, 27 falhas e 42 ignorados (812 no total). Os nomes das 27 falhas coincidem exatamente com `artifacts/sector-validation/existing-failures.txt`, sem novas falhas; a suíte completa portanto continua sem aprovação global. Os testes ignorados não contam como homologação. A instalação limpa e o upgrade são exercitados pelo gate PostgreSQL focado; a migration clínica também foi aplicada no banco sintético `sector_clean`.

As capturas da ação clínica em 360, 768, 1366 e 1920 pixels foram inspecionadas em `artifacts/sector-validation/clinical-ui/`. Essa revisão identificou e corrigiu a interferência do manipulador global de formulários no botão assíncrono; o teste agora exige uma segunda solicitação após o bloqueio de política. A inspeção cobre essa ação e sua mensagem, sem aprovar o visualizador PDF nativo ou todo o checklist visual do produto.

Continuam pendentes a homologação com provedores reais e OCR real, a validação final do visualizador PDF/zoom e do fluxo completo, além da resolução das falhas preexistentes da suíte. O provedor positivo dos gates clínicos é sintético; nenhuma credencial real foi usada e nenhuma política clínica foi habilitada automaticamente. A recuperação de upload foi testada com falhas injetadas em requisições e banco, sem encerramento físico do processo. As alterações permanecem locais, sem publicação nem migração de produção.

A repetição final dos gates de navegador aprovou 6/6 testes, sem ignorados. Evidências: `clinical-build.log`, `clinical-focused.trx`, `clinical-browser.trx`, `clinical-application.trx`, `clinical-results.json`, `upload-gate.json` e `clinical-ui/gate-results.json`, sob `artifacts/sector-validation/`.
