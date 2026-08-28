# Contract Measurement 2.0

## Objetivo
Centralizar produtividade, UST, faturamento, evidências, glosas e aceite mensal dos serviços GED, sempre no escopo do tenant.

## Rotas
O controller autorizado expõe `/ContractMeasurement`, catálogo, produtividade/criação, períodos/detalhes/itens/evidências, glosas, aceite, relatórios e exportação CSV. Todos os comandos POST usam antiforgery.

## Banco de dados
A migration idempotente `2026_08_28_contract_measurement_2.sql` cria as sete tabelas, restrições e índices. O seed global, de valor zero, cria o catálogo inicial sem simular faturamento.

## Catálogo de serviços
Inclui digitalização/OCR, indexação, classificação, tratamento, guardas, etiquetas, inventário, movimentação e revisões. Catálogos globais e do tenant são lidos sem aceitar IDs livres na UI.

## Produtividade
Quantidade positiva, preço não negativo e total calculado no servidor. Documento ou caixa, quando informados por integração, são validados no tenant. Os registros permanecem revisáveis antes da medição.

## Medição
Há uma competência por mês/ano/tenant. A geração agrupa lançamentos por serviço e calcula bruto, glosa e líquido. Estados suportados: `OPEN`, `GENERATED`, `SUBMITTED`, `APPROVED`, `REJECTED` e `CLOSED`.

## Glosas
Exigem período, motivo e valor dentro do bruto do item ou período. A resolução exige observação. Períodos aprovados/fechados ficam bloqueados.

## Aceite
Somente períodos com itens podem ser submetidos. Aprovação com glosa aberta exige justificativa; rejeição exige motivo; aprovação bloqueia valores. Todas as transições geram evento.

## Evidências
Podem referenciar período/item e origens documento, caixa, etiqueta e workflow. A tela guarda somente título, descrição sanitizada por apresentação, hash e referência, sem apresentar payload sensível.

## Relatórios
Tipos permitidos são produtividade por serviço/usuário, medição mensal, glosas, bruto versus líquido, serviços sem evidência e aceites pendentes. O SQL é selecionado por allowlist e sempre filtrado por tenant.

## Integrações
Etiquetas, OCR, inventário, movimentação e workflow podem usar `IContractProductivityService` para criar lançamentos `DRAFT` quando a configuração da instalação habilitar a integração. Nunca há faturamento ou aceite automático.

## Regras de negócio
Competência única, cálculo servidor, agrupamento por serviço, limites de glosa, evidência auditável e máquina de estados bloqueiam alterações indevidas.

## Segurança
`[Authorize]`, antiforgery, tenant em consultas e mutações, tipos de relatório fechados, referências validadas e ausência de IDs técnicos editáveis nas telas.

## Como validar
Aplique migration e seed, abra o módulo, registre produtividade, crie período, gere itens, adicione evidência/glosa, resolva a glosa, submeta e decida o aceite; exporte cada CSV. Execute `dotnet build InovaGed.sln -v:minimal`.

## Pendências futuras
Conectar os eventos de domínio de OCR, etiquetas, acervo e workflow após definir flags por contrato; adicionar assinatura externa do fiscal e testes end-to-end com PostgreSQL.
