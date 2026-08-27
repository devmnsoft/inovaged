# Physical Archive 2.0

## Objetivo
Central operacional para controlar caixas, endereços, inventários, empréstimos e cadeia de custódia com isolamento por tenant.

## Rotas
`/Physical` e `/Physical/Dashboard` exibem o painel. `/Physical/Boxes`, `/Physical/Locations`, `/Physical/Movements`, `/Physical/Inventory`, `/Physical/Loans` e `/Physical/Custody/{boxId}` cobrem a operação. Todos os POST usam antiforgery e seleção por listas, sem digitação de UUID.

## Banco de dados
A migration `2026_08_27_physical_archive_2.sql` é aditiva e idempotente. Ela mantém `ged.box` legado e cria o modelo 2.0 em `physical_box`, além de documentos vinculados, movimentos, sessões/itens de inventário, empréstimos e eventos de custódia. Índices são filtrados por registros ativos e tenant.

## Caixas e localizações
Caixas recebem código, etiqueta, período, retenção, estado e localização. A localização admite pai, tipo e capacidade, formando a árvore prédio/sala/corredor/estante/prateleira/posição.

## Movimentações
A operação troca a localização e grava movimento e evento de custódia na mesma transação.

## Inventário
Uma sessão pode abranger uma localização ou todo o acervo. O operador informa ou lê código/QR; o serviço classifica `FOUND`, `WRONG_LOCATION` ou `UNEXPECTED`, e permite fechamento auditável.

## Empréstimos
O empréstimo seleciona uma caixa, solicitante, setor e prazo. A caixa passa a `LOANED`; na devolução volta a `ACTIVE`, com eventos de custódia em ambos os passos.

## Cadeia de custódia
A timeline por caixa apresenta tipo, data, origem, descrição e correlação dos eventos físicos.

## Integrações
O cadastro legado de caixas e Documento 360 permanece disponível. A impressão usa `/Labels/PrintWizard?subjectType=BOX&subjectId={boxId}`. Eventos e estados expõem as condições `BOX_LOCATION_PENDING`, `INVENTORY_DIVERGENCE_REVIEW`, `LOAN_OVERDUE` e `BOX_LABEL_PENDING` para regras do Smart Workflow.

## Segurança
Controller autorizado, POST com antiforgery, consultas e mutações filtradas por tenant, sem exclusão física. As operações críticas geram custódia; o log existente continua responsável pela auditoria do cadastro legado.

## Como validar
Aplique as migrations requeridas, compile `InovaGed.sln`, autentique como administrador e percorra painel, caixas, localizações, movimentação, inventário, empréstimo/devolução e custódia. Valide também Etiquetas, Documento 360 e Administração.

## Pendências futuras
Aplicativo offline para coletores, importação em massa e reconciliação automatizada com sensores RFID.
