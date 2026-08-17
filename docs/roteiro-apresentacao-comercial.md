# Roteiro de apresentação e homologação comercial

Use exclusivamente o tenant e os documentos fictícios preparados para a demonstração. Antes da sessão, execute o setup, confirme `/SystemHealth` sem bloqueios e gere um backup válido.

| Etapa | Demonstração | Evidência de homologação |
|---|---|---|
| Login | Entrar com o administrador da demonstração e encerrar uma sessão anterior. | Acesso ao tenant correto, mensagem amigável para senha inválida e nenhuma credencial na URL/log. |
| Administração | Apresentar usuários, perfis e permissões. | Usuário sem `Administration.Manage` não altera configuração. |
| Upload e OCR | Enviar um PDF fictício, acompanhar processamento e abrir o detalhe. | Loading, sucesso/erro, versão original preservada e texto OCR pesquisável. |
| SmartSearch | Pesquisar uma pergunta prevista e outra sem resultado. | Respostas limitadas ao tenant/permissões, fontes visíveis e empty state útil. |
| Protocolo | Criar protocolo, tramitar e consultar histórico. | Destinatário obrigatório, estados coerentes e auditoria da tramitação. |
| Classificação e temporalidade | Classificar o documento e mostrar prazo/destinação. | Plano e tabela vigentes, justificativa e permissão para decisão crítica. |
| Acervo físico | Localizar caixa, posição e itens em `/Physical/Boxes`. | Busca, estado vazio e rastreabilidade física. |
| Faturamento hospitalar e glosas | Abrir visão consolidada e revisar uma glosa fictícia. | Valores mascarados quando aplicável, filtros e histórico da decisão. |
| Relatórios | Filtrar, visualizar e exportar um relatório. | `Reports.View`/`Reports.Export`, período explícito e arquivo consistente com a tela. |
| Auditoria | Localizar os eventos produzidos durante a sessão. | Ator, data, tenant, ação e correlação sem conteúdo sensível. |
| Continuidade e saúde | Exibir backup, banco, OCR, workers, storage, migrations e versão. | Backup validado, restauração apenas por operador autorizado e diagnóstico acionável. |

## Fechamento

1. Reabra o documento enviado e confirme OCR, classificação, protocolo e auditoria ponta a ponta.
2. Registre qualquer falha com rota, horário UTC, usuário, correlação e captura de tela sem dados sensíveis.
3. Não execute restauração durante uma apresentação; valide-a previamente em banco isolado com `Restore-InovaGed.ps1 -WhatIf` e em ensaio controlado.
