# Central de tramitação — diagnóstico reconfirmado e homologação

Estado lido nesta sessão, antes de tratar a entrega como pronta.

## Baseline

- Repositório local `C:\MNSOFT\inovaged`, branch `main`.
- A análise estática anterior usou o commit `04ed8cfa45fab870e5d76b38cdc09fea3edb46b3`. Esse commit não é ancestral do HEAD local. O blob de `InovaGed.Web/Program.cs` nesse commit é o texto `SKIP` (4 bytes). O arquivo real do HEAD foi preservado. Havia um merge em andamento dessa referência; ele não foi concluído nem publicado.
- Dois fluxos continuam distintos e não compartilham numeração:
  - `ged.protocolo`: mesa institucional em `/Protocolo` (central de tramitação).
  - `ged.protocol_request`: solicitações em `/Protocols` e `/ProtocolRequests`.
- Custódia física continua em `ged.physical_loan` e `ged.loan_request`. Encaminhamento digital não retira o documento físico.
- `ResolveSectorAsync` passa a devolver `ged.protocolo_setor.id` pelo vínculo `protocolo_usuario_setor`. Não devolve `ged.servidor.id`.

## Regras de estado

Processo e movimentação são independentes.

- Processo institucional: rascunho, aberto, em tramitação, deferido, indeferido, finalizado, arquivado, cancelado.
- Movimentação institucional: aguardando recebimento, recebida, devolução pendente, retorno confirmado, estornada.
- Processo da solicitação: solicitado, em análise, devolvido para ajuste, ajuste respondido, aprovado, rejeitado, finalizado, cancelado.
- “Devolvido para ajuste” não é devolução física.
- Enviar cria movimentação pendente e não altera o setor atual. O setor atual muda no recebimento ou na confirmação do retorno, na mesma transação do histórico.
- Receber exige a movimentação identificada. Se houver mais de uma pendência visível, o comando pede a escolha. Repetir o recebimento do mesmo usuário é idempotente. Outro usuário recebe conflito.
- Segundo encaminhamento para outro destino, com pendência ativa na mesma unidade, é bloqueado.
- Estorno é evento compensatório, só com a movimentação ainda pendente. Depois do recebimento, o caminho é a devolução. O evento original permanece.
- Processo encerrado não tramita. Reabertura exige administrador e justificativa. Arquivar protocolo não elimina documento nem executa temporalidade.

## Temporalidade

A decisão `APPROVE` permanece quando há bloqueio operacional. O item ganha `execution_status=BLOCKED`, origem e motivo. O caso:

- sem itens executados e com bloqueio: continua `APPROVED`, resultado `BLOCKED`;
- execução de todos os itens livres: `EXECUTED`;
- parte executada e parte bloqueada: `PARTIALLY_EXECUTED`.

Bloqueios mantidos: HOLD, sigilo e assinatura obrigatória. Bloqueios novos, só pelo vínculo do documento: empréstimo físico aberto, solicitação de empréstimo aberta, tramitação institucional ativa daquele protocolo/anexo e movimentação da solicitação daquele item. Confirmar retorno físico não dispara destinação.

## Anexos e GED

Falha de armazenamento ou de registro grava pendência com nome, etapa, motivo e atendimento. A tela não informa sucesso integral. O vínculo GED exige o mesmo tenant, anexo do protocolo e documento ativo. Se existir ACL do documento, o usuário precisa de leitura ou perfil administrativo. A remoção é lógica e auditada.

## O que esta sessão executou

- `dotnet build` de `InovaGed.Web` e `InovaGed.Application.Tests`: compilou, com os avisos já existentes do repositório.
- `dotnet test` filtrado em `ProtocolCustodyRulesTests`, `ProtocolRequestServiceSqlTests` e `MigrationManifestTests`: 23 aprovados, 0 falhas.
- PostgreSQL 16 descartável (container removido ao final): `apply_all_required_migrations.sql` parou na linha 2003 com `column "can_retry" does not exist`, antes de chegar na migration nova. Esse erro já estava no consolidado.
- A migration `2026_09_30_protocol_custody_center.sql`, aplicada duas vezes sobre tabelas mínimas, preencheu tramitação histórica como `RECEBIDA` sem pendência nova, criou o índice de custódia ativa e, com `ged.loan_request` presente, devolveu o protocolo de empréstimo `1` sob o lock da função.
- Não houve jornada HTTP com usuários de origem, destino e administração. Não houve verificação de tela no navegador. Não houve publicação, merge, push nem migration em produção.

## Homologação ainda necessária

1. Banco descartável. Aplicar `database/apply_all_required_migrations.sql` ou o migrador pelo manifesto, incluindo `database/migrations/2026_09_30_protocol_custody_center.sql` depois das migrations já existentes. Não aplicar em produção nesta tarefa.
2. Repetir o upgrade sobre uma cópia com protocolos antigos e conferir que os números não mudaram e que tramitações históricas ficaram como recebidas, não como pendência nova.
3. Provisionar usuários de origem, destino e administração pelo mecanismo já existente da aplicação, sem senha fixa no código.
4. Jornada: origem encaminha; destino vê “A receber” sem recebimento fictício; destino recebe; responsável, horário e histórico coincidem; usuário sem vínculo é negado; outro tenant não vê o registro; dois recebimentos simultâneos geram uma confirmação; clique repetido não duplica; segundo destino é bloqueado; lote com item inválido mostra impedimento antes de gravar; recebimento parcial mantém o saldo; estorno antes do recebimento preserva o evento; estorno depois do recebimento é negado e a devolução fica disponível; anexo com falha mostra atendimento e linha pendente; empréstimo ativo impede a execução da temporalidade sem converter `APPROVE` em `REJECT`; caso com todos os itens bloqueados não fica `EXECUTED`; execução parcial fica `PARTIALLY_EXECUTED`.
5. Conferir menu, atalhos F8/F9/F10 fora de campos de texto, impressão do manifesto e exportação da página sem observação interna de outro fluxo.
6. Enquanto os passos 1 a 5 não forem executados neste ambiente, a entrega não está homologada para produção.
