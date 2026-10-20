# Status das entregas — Demais Setores

Atualizado em 2026-10-19. Percentual não é calculado por arquivo alterado. Nenhum item está homologado.

## Sprint 0 — Auditoria

Situação: testado parcialmente, apenas como inspeção de código e teste de rótulo. O documento é `AUDITORIA_COMPLETA.md`.

1. Funcionalidades: leitura do controller, dos contratos, das três views principais, do script de cadastro, das migrations `2026_10_25` a `2026_11_06` e dos testes já existentes.
2. Problemas corrigidos: nenhum de banco nesta sprint. A auditoria registrou o que já funciona.
3. Regras de negócio: nenhuma regra de transição, permissão ou isolamento foi alterada.
4. Arquivos: `docs/demais-setores/AUDITORIA_COMPLETA.md` e este status.
5. Migrations: nenhuma criada e nenhuma aplicada.
6. Testes: a suíte histórica do módulo não foi reexecutada na auditoria.
7. TR: a matriz do Termo de Referência não foi marcada como atendida.
8. Pendências: reexecução dos testes de integração, navegador autenticado e homologação hospitalar.
9. Dependências: PostgreSQL descartável, host web reiniciado, origem clínica e parceiro quando o cenário exigir.
10. Riscos: tratar teste existente no código como prova desta sessão.

## Sprint 1 — Cadastro e envio, recorte executado

Situação: implementado, com teste parcial de regra. A tela não foi aberta.

1. Funcionalidades: cinco etapas visuais; texto de aplicabilidade, quantidade de arquivos e original físico no catálogo; cabeçalho Atlas.
2. Problemas corrigidos: a etapa não indicava em que ponto a pessoa estava; o select omitia colunas que a tabela já tinha; a conferência mostrava código interno. O último item também cobre a consulta.
3. Regras: o envio, a permissão `CADASTRAR`, o limite de parte e a confirmação de outra ocorrência permanecem. `Enviar` em memória não foi removido.
4. Arquivos alterados:
   - `InovaGed.Application/Sectors/SectorCatalog.cs`
   - `InovaGed.Application/Sectors/SectorRules.cs`
   - `InovaGed.Application.Tests/Sectors/DemaisSetoresRulesTests.cs`
   - `InovaGed.Web/Controller/DemaisSetoresController.cs`
   - `InovaGed.Web/Views/DemaisSetores/Cadastrar.cshtml`
   - `InovaGed.Web/Views/DemaisSetores/Consultar.cshtml`
   - `InovaGed.Web/Views/DemaisSetores/Parametrizar.cshtml`
   - `InovaGed.Web/wwwroot/js/demais-setores.js`
   - `InovaGed.Web/wwwroot/css/demais-setores.css`
5. Migrations: nenhuma.
6. Testes: `dotnet test` com filtro `DemaisSetoresRulesTests` em 2026-10-19. Resultado: 19 aprovados, 0 falhas, 0 ignorados, 137 ms. O projeto web compilou junto, com os avisos já existentes. Navegador não executado.
7. TR: não marcado como coberto.
8. Pendências: formatos e assinatura por tipo, retomada entre sessões, prova de arquivo grande, desativação segura de `Enviar` depois de confirmar que não há outro chamador.
9. Dependências: reinício do processo em `https://localhost:7282` para carregar view, script e CSS.
10. Riscos: o indicador de etapa é visual. A validação real continua no serviço.

## Sprints 2 a 14

| Sprint | Situação |
|---|---|
| 2 Consulta inteligente | Em desenvolvimento. Rótulos e cabeçalho feitos. Pesquisa, viewer e painéis já existiam e não foram refeitos. |
| 3 URL, iframe e API | Implementado, sem teste nesta sessão. Não refeito. |
| 4 OCR | Implementado no enfileiramento já existente, sem teste nesta sessão. Indicadores do módulo não iniciados. |
| 5 Extração inteligente | Parcial e bloqueado por política do contratante para provedor externo. Nenhum provedor novo foi ligado. |
| 6 Parametrização geral | Parcial. Contrato de atendimento e acesso existem. Cadastro livre de setores não iniciado. |
| 7 Conferência | Implementado, com teste parcial da regra de transição e do rótulo. Jornada completa de aprovação não foi ampliada. |
| 8 GED, protocolo e temporalidade | GED no vínculo já existe. Protocolo, temporalidade e assinatura do módulo não iniciados. |
| 9 Segurança e auditoria | Implementado nos controles já existentes, sem teste novo. |
| 10 Design | Em desenvolvimento. Cabeçalho Atlas e contraste da etapa atual. WCAG completo não iniciado. |
| 11 Banco e desempenho | Não iniciado como revisão de planos. Nenhuma migration aplicada no banco operacional. |
| 12 Homologação integral | Não iniciado. Os cinquenta cenários não foram executados. |
| 13 Dashboard | Não iniciado. Sem indicador fictício. |
| 14 Documentação da PoC | Em desenvolvimento. Auditoria e status escritos. Os demais arquivos pedidos ainda não existem. |

## Matriz curta

| Capacidade | Situação | Evidência desta sessão |
|---|---|---|
| Cadastro com paciente, atendimento e catálogo | Implementado, sem teste de tela | Código e auditoria |
| Upload em partes | Implementado, sem teste | Script `sendInParts`; teste de navegador não executado |
| Consulta e viewer | Implementado, sem teste de tela | `Consultar.cshtml` |
| Rótulo de conferência | Testado parcialmente | `SectorReview.Label` no teste de regras |
| Abertura opaca, iframe e API | Implementado, sem teste nesta sessão | Controller e `docs/demais-setores-integracao.md` |
| OCR na fila existente | Implementado, sem teste | `ConcluirEnvio` e `Reenfileirar` |
| IA clínica | Bloqueado por dependência de política e provedor | Formulário só com permissão e catálogo suportado |
| Parametrização completa de setores | Não iniciado além do contrato e do acesso | `Parametrizar.cshtml` |
| Protocolo, temporalidade e assinatura | Não iniciado no módulo | Serviço sem essas ligações |
| Dashboard | Não iniciado | Sem view |
| MV SOUL real | Bloqueado por dependência | Sem endpoint inventado |

## Evolução do prompt mestre — 2026-10-20

A numeração acima é a do módulo Demais Setores até 2026-10-19. O prompt mestre usa outra lista, registrada em `docs/ROADMAP_INOVAGED_NEXT_EVOLUTION.md`.

Nesta sessão o trabalho executado foi a Sprint 0 desse prompt (Quality Gate). Nenhuma funcionalidade nova de setor, template, dossiê ou indicador foi implementada. Os fluxos já existentes de cadastro, consulta, abertura integrada e conferência foram preservados.

Ajuste pontual no módulo: `PesquisarIntegrado` e `TrocarAtendimento` passaram a validar o token de antiforgery que `demais-setores.js` já enviava. `SectorDocumentRow`, `SectorContextRow` e `SectorEncounterOption` deixaram de ser `record` público materializado pelo Dapper e continuam aceitando a data que o Npgsql entrega como `DateTime`.

Teste desta sessão: filtro `DemaisSetoresRulesTests` incluído no conjunto de 26 testes aprovados em 2026-10-20. A tela não foi aberta. Nada desta tabela foi marcado como homologado.
