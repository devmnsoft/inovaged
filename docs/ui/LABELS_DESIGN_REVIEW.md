# Labels Premium Design Pass — revisão visual

Checklist de homologação do design, preview e impressão. A validação final em impressora física continua dependente do equipamento e do papel do cliente.

| Item | Problema encontrado | Correção aplicada | Status final | Pendência |
|---|---|---|---|---|
| `/Labels` | Atalhos sem hierarquia operacional completa | Hero, oito cards com status e orientação, guia e acesso às últimas impressões | Concluído | Validar dados recentes em ambiente integrado |
| `/Labels/PrintWizard` | Etapas e prévia não comunicavam todo o fluxo | Stepper Origem/Modo/Modelo/Prévia/Impressão, cards, preview lateral e ações explícitas | Concluído | Validar catálogo após migrations |
| `/Labels/History` | Auditoria precisava de leitura executiva | Hero, seis KPIs, filtros, tabela, badges e empty state Atlas | Concluído | Status de erro depende de persistência futura |
| `/Labels/LocDesk` | Formulário e modelo competiam visualmente | Editor em duas colunas, seleção amigável, grupos e preview aderente | Concluído | Validar conteúdo real do cliente |
| LocDesk padrão pasta | Impressão precisava compartilhar limpeza global | CSS de impressão carregado e modelo original preservado | Concluído | Prova física em papel |
| LocDesk padrão caixa | Folhas múltiplas precisavam preservar quebras | CSS de impressão compartilhado, agrupamento existente mantido | Concluído | Prova física em papel |
| LocDesk HOL | Hierarquia e linhas divergiam do anexo | Cabeçalho, contrato, controle/volume vermelhos, linhas e localização reforçados | Concluído | Comparação final com anexo em escala 100% |
| Modo impressão | Regras estavam fragmentadas | Menus, barras, botões, fundos e sombras removidos no CSS exclusivo | Concluído | Margens podem exigir calibração por driver |
| Mobile | Grades rígidas em telas menores | Breakpoints para cards, editor, histórico e ações | Concluído | Teste adicional em dispositivo físico |
| Ações | Submits podiam ser ambíguos | `type`, `formaction` e `formmethod` explícitos | Concluído | Nenhuma |
| Badges | Estados pouco distinguíveis | Estados operacionais e tipos com cores semânticas | Concluído | Erro depende de dado de backend |
| Empty states | Ausência de orientação | Mensagem contextual e CTA para nova impressão | Concluído | Nenhuma |

## Rotas revisadas

Existem actions/views para Central, PrintWizard, History, LocDesk, modelos LocDesk, Calibration, Batch, Print e Preview. `Print` e `Preview` são endpoints POST do assistente, portanto não devem ser validados por navegação GET direta nem substituídos por rotas artificiais.

## Roteiro manual

1. Acessar Central, PrintWizard, History, LocDesk, Calibration e Batch autenticado.
2. Alternar entre LocDesk padrão pasta, caixa e HOL e conferir a prévia.
3. Gerar a prévia, imprimir em escala 100% e verificar QR Code, bordas e quebras.
4. Gerar uma etiqueta e reimprimi-la informando justificativa.
