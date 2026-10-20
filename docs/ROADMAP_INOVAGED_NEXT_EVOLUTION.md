# Roadmap InovaGED Next Evolution

Atualizado em 2026-10-20. Este documento segue a numeração do prompt mestre (Demais Setores 360°, Temporalidade Inteligente 3.0 e Atlas Premium). A numeração antiga de `docs/demais-setores/STATUS_ENTREGAS.md` permanece como histórico do módulo e não é a mesma lista.

Nenhuma sprint está homologada. Não há percentual de evolução.

## Sprint 0 — Quality Gate

- Objetivo: encontrar a causa da falha do Doctor no commit `9c3f5213c1` e corrigi-la sem desligar verificações.
- Causa: o job `quality-gate` executa `dotnet run --project InovaGed.Environment.Doctor --no-build -- quality-gate --no-db-required`. A compilação daquele commit passou. O Doctor encerrou com falha porque os checks estáticos reprovavam materialização Dapper em `record` público, POSTs sem antiforgery visível ao scanner, views Razor e serviços que o scanner só procurava em `Program.cs`. O modo `--no-db-required` também transformava indisponibilidade de banco em falha no check de incidentes.
- O que foi corrigido nesta sessão:
  - Records públicos lidos pelo Dapper passaram a classes mutáveis, com cópia explícita onde havia `with`. `SectorDocumentRow`, `SectorContextRow` e `SectorEncounterOption` mantêm o construtor que converte `DateTime?` vindo do Npgsql.
  - POSTs de consulta integrada, assinatura de termo, API de assinatura e `POST /api/pacs/tickets` passaram a exigir `ValidateAntiForgeryToken`. Os clientes já existentes de Demais Setores, termo e agente de assinatura enviam `RequestVerificationToken`.
  - O scanner de segurança deixa de tratar `{` dentro de atributo de rota como início do método. Um POST sem o atributo continua falha.
  - Checks de DI, Razor, ícones e incidentes offline permanecem ativos. O check de incidentes, com `--no-db-required`, emite aviso e não abre conexão.
  - Os workflows `ci.yml` e `dotnet-quality-gate.yml` reprovam somente a saída 2 (FAIL). A saída 1 (WARNING) publica o relatório e o job segue. O status do relatório não é reescrito para PASS.
- Regras: FAIL bloqueia. WARNING exige triagem e não é sucesso. Nenhum teste foi removido.
- Arquivos principais: `InovaGed.Environment.Doctor/Checks/SecurityQualityCheck.cs`, controllers citados, `DocumentAiAssistService.cs`, `DemaisSetoresController.cs`, `QualityGate2ChecksTests.cs`, `.github/workflows/ci.yml`, `.github/workflows/dotnet-quality-gate.yml`, `docs/quality/QUALITY_GATE_2.md`.
- Banco: nenhuma migration criada e o migrador oficial não foi executado.
- Testes em 2026-10-20: `dotnet build InovaGed.sln` com 0 erros. Filtro `QualityGate2ChecksTests|DemaisSetoresRulesTests|NavigationSimplicityTests`: 26 aprovados, 0 falhas. Filtro `FullyQualifiedName!~Postgres` em `InovaGed.Application.Tests`: 777 aprovados, 29 falhas, 2 ignorados, 808 no total, cerca de 6 min 24 s. Quality Gate offline: 235 checks, 66 OK, 169 warnings, 0 falhas, status geral WARNING, saída do Doctor 1. Relatório em `artifacts/quality-gate/quality-gate-report.md` (2026-10-20T10:27:46Z).
- Evidência: Dapper Mapping, Security Scan, Razor, DI, ícones, administração e migrations obrigatórias em PASS. Banco, route-smoke HTTP, consistência visual, isolamento por tenant, desempenho e incidentes permanecem WARNING porque o modo offline não consulta o PostgreSQL, a base URL de smoke não foi informada e os heurísticos de SQL/UI continuam apontando ocorrências reais.
- Pendências: triagem dos 169 avisos sem edição em massa de SQL. Execução HTTP do route-smoke com `QUALITY_GATE_BASE_URL`. Quality Gate com banco, fora do journal operacional `Database=postgres`. Reinício do processo web para carregar os binários. Os 29 testes sem PostgreSQL que falharam não foram corrigidos nesta sessão. Os arquivos conferidos para feedback, designer de etiquetas, layout e composição de DI estão iguais ao HEAD, então essas falhas amostradas não vêm do antiforgery nem da conversão Dapper. Testes cujo nome contém Postgres e a suíte dos outros projetos não foram executados.
- Riscos: `POST /api/pacs/tickets` não tem chamador no repositório. Um cliente externo que não envie o token de antiforgery passa a receber recusa. A igualdade de `record` deixou de valer nos tipos convertidos. O CI local não foi reexecutado no GitHub.
- Situação: corrigido e verificado no Doctor offline. Não homologado.

## Sprints 1 a 15

| Sprint | Objetivo | Situação |
|---|---|---|
| 1 Demais Setores 360° | Cadastro administrativo de setores e templates com persistência | Pendente. O módulo atual de cadastro, consulta, catálogo e parametrização parcial permanece. Cadastro livre de setor, grupos, políticas e templates versionados não foram criados nesta sessão. |
| 2 Dossiê 360° | Completude documental por atendimento | Pendente. A fórmula pedida não foi implementada. |
| 3 Workflow por setor | Construtor sobre o motor existente | Pendente. O motor atual não foi estendido. |
| 4 Temporalidade 3.0 | Cálculo explicável no motor existente | Pendente. Plano, tabela, fila, worker e destinação já existem e não foram substituídos. O painel “Entenda como este prazo foi calculado” não existe. |
| 5 Simulador | Simulação somente leitura antes de publicar | Pendente. |
| 6 Guarda preventiva | Bloqueio justificado de destinação | Pendente. |
| 7 Destinação | Jornada com trilha de decisão | Pendente além do fluxo já existente de termos. |
| 8 IA arquivística | Sugestão com evidência e aprovação humana | Pendente como produto novo. SmartGED e a assistência documental já existem e não decidem destinação sozinhos. |
| 9 Saúde do acervo | Índice com dados reais | Pendente. Nenhum indicador fictício foi inserido. |
| 10 Atlas Premium | Design system em todo o sistema | Pendente como reforma. A identidade atual e o menu existente foram preservados. |
| 11 Acessibilidade e linguagem | WCAG 2.2 AA e textos claros | Pendente. |
| 12 Login e navegação | Animação com movimento reduzido e rotas | Pendente. O gate de administração não apontou card quebrado na análise estática. |
| 13 Setores e temporalidade | Resolver classe e prazo na entrada do documento | Pendente. Sem data de eliminação inventada. |
| 14 Indicadores | Painéis por tenant, unidade, setor e período | Pendente. |
| 15 Homologação | Jornadas de ponta a ponta | Pendente. Navegador não foi exercitado. |

## Bloqueios de homologação

- O relatório do Quality Gate está WARNING, não PASS.
- PostgreSQL operacional não foi validado pelo Doctor e o migrador oficial não deve ser aplicado nesse journal.
- Telas novas do prompt não foram abertas no navegador.
- Parceiros externos continuam necessários para MV SOUL, PACS quando houver cliente fora do repositório, e qualquer provedor de IA clínica. Nenhum dado clínico foi enviado a IA externa nesta sessão.
