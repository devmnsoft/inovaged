# Checklist de QA visual — Premium UI Pass 3

| Rota / cenário | Status | O que validar | Observações |
|---|---|---|---|
| `/Administration` | Pronto para QA | Hero, quatro atalhos, seis status e agrupamento executivo. | Conferir valores reais do ambiente. |
| `/Administration/Users` | Pronto para QA | Breadcrumb, toolbar/navegação, badges, tabela, ações e vazio. | Testar usuário sem tenant. |
| `/Administration/Tenants` | Pronto para QA | Isolamento, status e scroll da tabela. | Requer full admin para cenário completo. |
| `/Administration/Security` | Pronto para QA | Catálogo, privacidade, alertas e textos longos. | Exercitar busca de permissão. |
| `/Administration/Migrations` | Pronto para QA | Estados OK, atenção e indisponível; empty state. | Usar ambiente parcialmente migrado. |
| `/Administration/Workers` | Pronto para QA | Estado, última atividade e detalhes extensos. | Simular worker degradado. |
| `/Labels` | Pronto para QA | Oito cards, badges, ícones, destinos e alerta. | Validar foco por teclado. |
| `/Labels/PrintWizard` | Pronto para QA | Cinco etapas, troca de catálogo, resumo, preview e quatro ações. | Validar erros obrigatórios e justificativa. |
| `/Labels/LocDesk` | Pronto para QA | Formulário, abas, dados carregados e ação principal. | Preservar “LOCDESCK”. |
| `/Labels/History` | Pronto para QA | Cinco KPIs, filtros, badges, detalhes e vazio. | Conferir totais com a consulta. |
| `/Labels/Boxes` | Pronto para QA | Localização ausente, ações, vazio e scroll. | Validar títulos em 320 px. |
| `/Labels/Documents` | Pronto para QA | Modelos padrão/LocDesk, ações e títulos longos. | Conferir limite de 300 itens. |
| Impressão LocDesk caixa/pasta | Pronto para QA | Borda, logo, QR, vermelho, margens em mm e duas caixas/página. | Desabilitar cabeçalho/rodapé do navegador. |
| Desktop 1440/Notebook 1280 | Pronto para QA | Grades, alinhamento, densidade e largura do conteúdo. | Sem sobreposição de botões. |
| Tablet 768 | Pronto para QA | Quebra de cards, toolbar e escala da prévia. | Testar retrato e paisagem. |
| Mobile 320–430 | Pronto para QA | Hero, botões, scroll de tabelas e sidebar recolhida. | Navegação não deve cobrir conteúdo. |
