# Auditoria do módulo Demais Setores

Data da inspeção: 2026-10-19.  
Repositório local: `C:\MNSOFT\inovaged`.  
Escopo: código, views, JavaScript, migrations registradas e testes existentes.  
Nenhuma funcionalidade abaixo está homologada. Esta auditoria não reexecutou a suíte do módulo nem abriu o host autenticado. Os testes citados existem no código; o resultado desta sessão está em `STATUS_ENTREGAS.md`.

Nada foi reescrito só porque o prompt descreve a jornada de novo. O módulo já cobre cadastro, consulta, conferência, upload em partes, abertura opaca, iframe e API de integração.

## Situação usada nesta auditoria

| Situação | Significado nesta inspeção |
|---|---|
| Implementado, sem teste | O fluxo existe no código. A suíte correspondente não foi executada nesta auditoria. |
| Testado parcialmente | Houve teste automatizado nesta sessão, ou a cobertura existente foi conferida só em fonte, sem navegação autenticada. |
| Não iniciado | Não há fluxo correspondente no módulo. |
| Bloqueado por dependência | Depende de parceiro, origem clínica, certificado ou homologação externa. |
| Homologado | Não utilizado. |

## Mapa do que já existe

| Peça | Onde está |
|---|---|
| Tela de cadastro | `InovaGed.Web/Views/DemaisSetores/Cadastrar.cshtml`, rota `/DemaisSetores/Cadastrar` |
| Tela de consulta | `Consultar.cshtml`, rota `/DemaisSetores/Consultar` |
| Parametrização | `Parametrizar.cshtml`, rota `/DemaisSetores/Parametrizar` |
| Página do parceiro | `Parceiro.cshtml` |
| Prévia de entrega | `PreviaEntrega.cshtml` |
| Recuperação | `Recuperar.cshtml` |
| Controller MVC | `InovaGed.Web/Controller/DemaisSetoresController.cs`, política `AppPolicies.DemaisSetores` |
| API | `DemaisSetoresIntegracaoController`, rota `api/integracoes/demais-setores` |
| Serviço | `InovaGed.Infrastructure/Sectors/DemaisSetoresService.cs`, contrato `IDemaisSetoresService` |
| Abertura | `ISectorOpeningService` e implementação de abertura, ticket e sessão |
| Menu | Sidebar, grupo Demais setores: Cadastrar, Consultar e Parametrizar. Componente `DemaisSetoresMenuViewComponent` |
| Cliente | `wwwroot/js/demais-setores.js` e `demais-setores-session.js` |
| Estilo | `wwwroot/css/demais-setores.css`. O cabeçalho Atlas passou a ser usado nas três telas principais nesta entrega |
| Contrato já publicado | `docs/demais-setores-integracao.md` |
| Migrations | `2026_10_25` a `2026_11_06`, prefixo `demais_setores`, no manifesto e em `required_migrations.json` |

O envio visível na tela usa `IniciarEnvio`, `ParteEnvio`, `SituacaoEnvio`, `CancelarEnvio` e `ConcluirEnvio`. A ação `Enviar` ainda lê o arquivo inteiro em memória e não é chamada pelo JavaScript atual. Ela permanece para não quebrar outro chamador.

## Tabelas

Criadas de forma idempotente nas migrations do módulo:

- Catálogo e perfil: `ged.sector_document_type`, `sector_type_mapping`, `sector_profile`, `sector_profile_item`, `sector_document_synonym`.
- Contexto clínico local: `sector_patient`, `sector_patient_alias`, `sector_encounter`, `sector_kind_contract`.
- Acesso: `sector_access`, `sector_type_scope`, `sector_module_setting`.
- Documento: `sector_document_link`, `sector_review_event`, `sector_link_correction`, `sector_extracted_field`, `sector_field_review`, `sector_finding`.
- Entrega e incorporação: `sector_delivery`, `sector_delivery_item`, `sector_embed_origin`, `sector_embed_grant`.
- Abertura: `sector_integration`, `sector_open_session`, `sector_open_code`, `sector_entry_ticket`, `sector_open_request`.
- Processamento e extração clínica: `sector_processing_state`, `sector_clinical_extraction`, `sector_clinical_extraction_segment`.

A lista de documentos da consulta filtra `tenant_id` e aplica `AbacAuthorizationService.DocumentAccessPredicate`. O catálogo visível vem de `VisibleCatalogCodesAsync`.

## Funcionalidades

### 1. Identificar paciente e atendimento

- Requisito: pesquisar por CPF ou identificador institucional, sem exigir o UUID interno, e listar atendimentos do mesmo paciente.
- Implementação: `POST /DemaisSetores/BuscarPaciente` com `identifierKind` `CPF` ou `ID`. A tela também envia sistema e unidade de origem. O padrão visual da origem é `MV`, mas isso não cria um conector do MV SOUL.
- Serviço: `FindPatientAsync`. Sem `ClinicalSource` habilitada, a origem externa fica indisponível e não inventa paciente.
- Tabelas: `sector_patient`, `sector_encounter`, `sector_patient_alias`, `sector_kind_contract`.
- Tela: `Cadastrar.cshtml` e a busca da `Consultar.cshtml`.
- Permissão: entrada no módulo. O cadastro posterior exige `CADASTRAR`.
- Testes no repositório: regras de código externo e origem clínica desabilitada em `DemaisSetoresRulesTests`; portões de navegador em `DemaisSetoresConsultBrowserTests` e `SectorUploadGateBrowserTests`. Não reexecutados nesta auditoria, salvo o teste de rótulo acrescentado nesta sessão.
- Problema: a jornada visual era uma página única com três rótulos. O comportamento já era o de etapas.
- Correção desta entrega: cinco indicadores (paciente, atendimento, documento, envio, finalização) ligados ao estado já existente. Os atributos `data-ds-*` não foram renomeados.
- Situação: implementado, sem teste de ponta a ponta nesta auditoria.

### 2. Escolha do documento do catálogo

- Requisito: mostrar nome, obrigatoriedade, formatos, vários arquivos e original físico, só para o setor e o perfil autorizados.
- Implementação: `CatalogTypesAsync` lista tipos ativos cujo código está em `VisibleCatalogCodesAsync`. O seed do Anexo I tem 33 tipos e não sobrescreve linha com `customized=true`.
- Serviço e tabelas: `ged.sector_document_type` (`applicability`, `multiple_files`, `physical_original`, `view_sectors`).
- Tela: select `data-ds-catalog`.
- Permissão: códigos visíveis ao usuário. Administrador pleno vê o conjunto autorizado pela regra já existente.
- Problema: a opção mostrava só ordem e nome. Formatos aceitos, assinatura, prazo e classe arquivística não estão no catálogo do setor.
- Correção desta entrega: a consulta passa a ler aplicabilidade, vários arquivos e original físico. A tela explica esses três pontos em português. Formato continua no `accept` do input, não por tipo.
- Situação: parcial. Teste de rótulo de aplicabilidade executado com as regras; a tela não foi aberta.

### 3. Envio em partes

- Requisito: arrastar, vários arquivos, progresso, cancelamento, retomada, hash, idempotência e arquivo grande fora da memória.
- Implementação: `PrepareUploadAsync`, `IUploadChunkService`, limite de parte de 20 MB, conclusão em `AttachCompletedUploadAsync`. O JavaScript é o único caminho da tela (`sendInParts`). Há cancelamento e consulta das partes faltantes.
- Tabelas: sessão de upload existente do GED e `sector_document_link`.
- Permissão: `CADASTRAR`.
- Testes no repositório: `SectorUploadGateBrowserTests` cobre falha de gatilho, situação e parte. Não reexecutado aqui. Não houve prova de arquivo de 10 GiB.
- Problema que permanece: `Enviar` ainda bufferiza o arquivo. Retomada depende de consultar a sessão já criada; não há um retomador independente de outra aba. Duplicidade e versão são tratadas no serviço, com estágios `DUPLICIDADE` e `MULTIPLICIDADE`, e a tela oferece vincular de novo.
- Situação: implementado, sem teste nesta auditoria. O limite de 10 GiB não está homologado.

### 4. Consulta, visualização e pesquisa

- Requisito: três regiões, PDF, texto, dados extraídos, filtros e paginação no servidor, sem vazar tenant.
- Implementação: `Consultar` monta lista, campos, achados e checklist. Abas Documentos, PDF e Análise. O script recolhe a análise, troca zoom e abre o arquivo em iframe. A pesquisa `Pesquisar` é paginada no servidor, 20 itens, até 51 páginas. Filtros de nome, origem, setor e conferência são locais sobre a lista já autorizada.
- Endpoints: `Consultar`, `Arquivo`, `Texto`, `Pesquisar`, `AtualizarLeitura`.
- Serviço: `SearchAsync`. A lista SQL usa tenant, atendimento, catálogo visível e predicado ABAC.
- Tabelas: `sector_document_link`, `sector_document_type`, `sector_extracted_field`, `sector_finding`, documento e versão do GED.
- Permissão: `CONSULTAR` para entrar; `Documents.View` no predicado ABAC; conferência exige `CONFERIR` ou `CADASTRAR` conforme a transição.
- Testes no repositório: `DemaisSetoresConsultBrowserTests`. Não reexecutados aqui.
- Problema: a situação de conferência aparecia com o código interno. Busca por conteúdo OCR de todo o módulo, com filtro de classificação e índice dedicado novo, não foi acrescentada nesta entrega. A busca existente é literal no atendimento, com sinônimo do tipo.
- Correção desta entrega: rótulos de conferência em português na lista, no filtro e no formulário. O valor enviado continua sendo o código.
- Situação: implementado, sem teste de navegador nesta auditoria.

### 5. Conferência, correção, extração e entrega

- Requisito: recebimento, extração, conferência, correção e rastreio. Divergência com justificativa.
- Implementação: `Conferir`, `CorrigirVinculo`, `RevisarCampo`, `DecidirAchado`, `EvidenciaFisica`, `PrepararEntrega`, `ConfirmarEntrega`. `SectorReview.CanTransition` bloqueia transição inválida. Não aplicável exige justificativa.
- Tabelas: `sector_review_event`, `sector_link_correction`, `sector_field_review`, `sector_finding`, `sector_delivery`, `sector_delivery_item`.
- Permissões: `CONFERIR`, `CORRIGIR_VINCULO`, `REVISAR_EXTRACAO`, `IMPRIMIR`, `COMPARTILHAR`.
- Testes no repositório: `DemaisSetoresRulesTests` e `SectorDeepStabilizationAndIntegrityTests`. O teste de transição e de rótulo foi executado nesta sessão.
- Problema: a entrega registra protocolo e evidência de entrega. Isso não tramita o módulo de Protocolo. Não há eliminação nem transferência. A frase gravada na entrega diz que a impressão não afirma preservação de assinatura.
- Situação: conferência e correção implementadas, com teste parcial de regra. Protocolo institucional e assinatura ICP-Brasil no módulo: não iniciado.

### 6. OCR e processamento

- Requisito: aceitar o arquivo, enfileirar OCR sem bloquear a navegação e mostrar falha em português.
- Implementação: `ConcluirEnvio` enfileira OCR para pdf, jpg, jpeg, png, tif e tiff usando a fila já existente. A consulta mostra prévia, OCR e extração com rótulos próprios. `Reenfileirar` repete a fila. `sector_processing_state` guarda estado do módulo.
- Problema: não há painel de indicadores do módulo. A central de processamento do GED é outra tela. Não foi criada segunda fila.
- Situação: implementado no fluxo de conclusão, sem teste nesta auditoria. Métricas exclusivas do módulo: não iniciado.

### 7. Extração e IA clínica

- Requisito: sugerir campos com confiança e evidência, sem diagnóstico e sem alterar dado crítico em silêncio.
- Implementação: `ExtrairItensClinicos` só aparece com `CanExtract` e catálogo em `SectorClinicalEvidence.SupportedCatalog`. O texto da tela diz que a sugestão exige revisão e não gera cobrança. A revisão humana grava valor anterior pelo fluxo `RevisarCampo`.
- Provedores: o caminho governado já existente. Esta entrega não adicionou Groq, Gemini, DeepSeek nem envio novo de texto clínico.
- Situação: parcial e bloqueado por política, provedor e base legal do contratante quando a origem for externa. Testes de regra de prescrição existem e não foram reexecutados aqui.

### 8. Abertura por URL, iframe e API

- Requisito: parceiro autenticado, ticket opaco, origem permitida, `frame-ancestors`, `postMessage` com origem verificada e bloqueio de ticket vencido ou reutilizado.
- Implementação: `GET /DemaisSetores/Integrar` grava ticket e não devolve conteúdo clínico. Continuidade em `/DemaisSetores/Continuar/{ticketId}`. Abertura em `/DemaisSetores/Abrir/{code}`. Sessão em `/DemaisSetores/ConsultaIntegrada/{publicId}`. API `POST aberturas`, `GET aberturas/estado`, `POST aberturas/recuperar`. Cadastro e desativação em `CadastrarIntegracao` e `DesativarIntegracao`. Diagnóstico em `DiagnosticarAbertura`.
- Contrato público já escrito: `docs/demais-setores-integracao.md`. Parâmetros compatíveis: `identifierKind`, `identifier`, `encounterCode`, `documentName`, `documentCode`, `clientId`.
- Segurança já descrita nesse contrato: CPF fora da URL de retorno, cookie `ds_ctx_{publicId}` insuficiente sozinho, fragmento de proteção, allowlist de origem.
- Testes no repositório: `SectorEntryAndOpeningGateTests`, `SectorOpeningExchangeTests`, `SectorUniversalIntegrationTests`, `SectorRequestAuditTests`, `SectorPartnerGateBrowserTests`, `DemaisSetoresConsultBrowserTests`. Não reexecutados nesta auditoria. Registro anterior de 2026-10-14, fora desta sessão, citou três testes de portão, dois de troca e o HTML do parceiro. Isso não homologa o host atual.
- MV SOUL: não há endpoint nativo. A origem clínica só existe se `ClinicalSource` estiver configurada. A chave não entra em log nem em JavaScript.
- Situação: implementado, sem teste nesta auditoria. Homologação com o hospital: bloqueada por dependência.

### 9. Parametrização

- Requisito: cadastrar setores, catálogo, perfis, OCR, revisão, temporalidade e histórico sem alterar documento finalizado.
- Implementação atual: lista departamentos já cadastrados, contrato de tipo de atendimento (`SalvarContrato`), concessão e revogação de acesso (`Conceder`, `RevogarAcesso`), origem de incorporação (`Origem`) e Anexo II (`HabilitarAnexoIi`). Setores fixos na concessão: Faturamento, Farmácia, DDI, CME e Selecionado.
- Problema: não há cadastro geral de Farmácia, CME, DDI, laboratório, SAME e os demais setores pedidos. Não há versão de configuração ligada a temporalidade. O seed não reescreve tipo personalizado.
- Correção desta entrega: cabeçalho Atlas e texto de orientação. Nenhuma regra de acesso foi afrouxada.
- Situação: parcial. Teste de interface não executado.

### 10. GED, protocolo, temporalidade e assinatura

- GED: o vínculo grava documento e versão na estrutura já existente, com hash e ACL do setor. Pesquisa do módulo não substitui a pesquisa geral do GED.
- Protocolo: a confirmação de entrega audita um número informado na entrega. Não abre processo em `/Protocolo`.
- Temporalidade: o serviço do módulo não consulta `classification_plan_version_item` nem calcula eliminação. Nenhum documento é eliminado por este módulo.
- Assinatura: não há verificação criptográfica ICP-Brasil neste módulo. A entrega declara que não afirma assinatura válida.
- Situação: GED no vínculo, implementado sem teste nesta auditoria. Protocolo, temporalidade e assinatura do módulo: não iniciado. Não foram inventados endpoints.

### 11. Segurança, auditoria e desenho

- Autenticação reutiliza a identidade do InovaGED. Parametrizar exige `PARAMETRIZAR` ou administrador pleno. A lista não confia só no identificador enviado: repete tenant, catálogo visível e ABAC.
- Auditoria do serviço registra ação, tenant e resumo. A regra já existente evita devolver conteúdo clínico na abertura.
- Desenho: as telas usavam o sistema visual próprio do módulo. Cabeçalhos Atlas foram aplicados em Cadastrar, Consultar autenticado e Parametrizar. A consulta incorporada mantém o layout do frame e um título próprio, para não depender do CSS do shell.
- Situação: controles existentes implementados, sem teste de segurança novo nesta auditoria. Revisão WCAG completa: não iniciada.

### 12. Dashboard operacional

- Não há ação nem view de indicadores do módulo.
- Situação: não iniciado. Não foram criados números fictícios.

## Correções desta entrega

- Cinco etapas visuais no cadastro, sem trocar o contrato do JavaScript.
- Orientação do tipo documental a partir de colunas que já existiam.
- Rótulos de conferência em português, com o código preservado no valor do formulário.
- Cabeçalho Atlas nas três páginas operacionais autenticadas.
- Teste de unidade dos rótulos e da frase de aplicabilidade desconhecida.

## Pendências reais

- Reexecutar a suíte de setores e a suíte de navegador em ambiente descartável, com paciente fictício.
- Abrir as telas no host autenticado depois de reiniciar o processo web.
- Não aplicar migrations no `Database=postgres` operacional por este trabalho. Nenhuma migration nova foi criada.
- Parametrização geral de setores, dashboard, vínculo com temporalidade, tramitação de protocolo e verificação de assinatura.
- Prova de upload grande, retomada entre reinícios e iframe com cookie de terceiro bloqueado.
- Conector real do MV SOUL, somente com contrato homologado.

## Riscos

- Dois caminhos de envio: a tela usa partes; `Enviar` ainda materializa o arquivo.
- Catálogo do Anexo I não é a lista institucional de todos os setores. DDI não recebe significado automático.
- Abrir a consulta incorporada sem o processo novo continua no binário anterior.
- Classificar qualquer item como homologado a partir desta leitura seria incorreto.
