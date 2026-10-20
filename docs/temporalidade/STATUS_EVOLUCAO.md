# Status da evolução — Temporalidade

Atualizado em 2026-10-20. Nenhum item abaixo está homologado. Não há percentual.

## O que já existe e foi preservado

O motor atual continua sendo a fonte do prazo: plano de classificação, versão publicada, tabela de temporalidade, eventos condicionantes, fila de revisão, `RetentionDailyWorker`, recálculo e destinação por termo. A explicação já persistida usa `retention_basis_at` e `retention_due_at`. A data de upload não foi adotada como marco inicial. Nenhuma data de eliminação foi inventada.

## Sprint 4 — Temporalidade Inteligente 3.0

Situação: não iniciada como evolução nova.

- Funcionalidades pedidas e ainda ausentes: painel com classe, código, versão do instrumento, evento inicial, prazos, destinação, base normativa, data calculada, impedimentos e responsável; botão “Entenda como este prazo foi calculado”.
- Regras novas: nenhuma. As regras vigentes do motor não foram alteradas.
- Arquivos: nenhum arquivo de temporalidade foi modificado para essa sprint.
- Banco: nenhuma migration. O migrador oficial não foi executado no journal operacional.
- Testes: nenhum teste novo de cálculo. O Quality Gate offline não consulta o PostgreSQL.
- Pendências: composição determinística versionada, evento ausente explícito, impedimento de prazo negativo e justificativa de alteração.
- Riscos: criar um segundo motor. Isso permanece proibido.

## Sprints 5 a 7 e 9

| Sprint | Situação |
|---|---|
| 5 Simulador Arquivístico 360° | Não iniciado. Não há registro de simulação somente leitura. |
| 6 Guarda Preventiva Inteligente | Não iniciada. Não há bloqueio novo com justificativa, evidência e liberação controlada. |
| 7 Destinação controlada | O termo de destinação já existente foi mantido. A trilha explicável pedida não foi acrescentada. A assinatura do termo passou a exigir o token de antiforgery, e a tela envia esse token. |
| 9 Saúde do Acervo 360° | Não iniciada. Nenhum índice ou gráfico fictício foi gravado. |

## Evidência de 2026-10-20

Quality Gate offline com 0 falhas e status WARNING. A suíte de temporalidade com PostgreSQL não foi executada. A tela de temporalidade não foi aberta no navegador.