# Labels Visual Designer 2.0

## Objetivo
O Editor Visual de Etiquetas configura campos, dimensões em milímetros, validações e versões sem substituir as views oficiais de impressão do InovaGED e do LocDesk.

## Rotas
Todas exigem autenticação administrativa. A listagem está em `GET /Labels/Designer`; detalhes, edição, versões, preview e teste usam `GET /Labels/Designer/{templateCode}[/{Edit|Versions|Preview|PrintTest}]`. As operações `Save`, `Duplicate`, `Publish` e `Validate` são POST protegidos por antiforgery.

## Banco de dados
A migration idempotente `database/migrations/2026_08_31_label_visual_designer_2.sql` cria design, campos, versões, validações e trilha de auditoria no schema `ged`, com índices tenant-aware.

## Modelos oficiais
`FACTORY_BOX_V1`, `FACTORY_DOCUMENT_V1`, `LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_V1` e `LOCDESK_PASTA_HOL_V1` são publicados pelo seed como `is_system_template=true`. São somente leitura. As views fiéis já existentes continuam responsáveis pela impressão oficial; em especial, `ARQUIVO LOCDESCK ANANINDEUA` não é alterado.

## Modelos personalizados
Duplicar copia dimensões e campos para um código `CUSTOM_*`, status `DRAFT` e tenant atual. Somente rascunhos personalizados podem ser salvos. A publicação exige uma validação aprovada e cria snapshot versionado imutável.

## Editor visual
A tela tem paleta à esquerda, canvas central com régua, grade e margem segura, e propriedades à direita. Nesta versão, seleção e edição numérica substituem drag-and-drop.

## Campos
Cada campo armazena chave, rótulo, tipo, fonte de dados, X/Y, largura/altura, tipografia, alinhamento, cor, obrigatoriedade e condição de impressão.

## Preview
O preview posiciona dados representativos em escala aproximada. Dados de amostra não são persistidos.

## Impressão de teste
`PrintTest` usa documento sem shell, navegação ou botões e exibe `AMOSTRA DE TESTE` fora da etiqueta. Essa marca não participa da impressão real.

## Validações
São detectados nome/dimensão ausentes, campo fora da área, sobreposição, fonte ilegível, QR pequeno e campos obrigatórios. LocDesk e HOL possuem conjuntos específicos, incluindo contrato, controle, localização, assunto, classificação e borda no HOL.

## Integração com PrintWizard
O catálogo mínimo permanece disponível. Designs `PUBLISHED` complementam o catálogo e a definição tenant mais recente prevalece por código. O snapshot da impressão registra nome, código e versão.

## Integração com History
O histórico mostra código, nome, versão e origem Designer. Registros legados exibem `Versão não registrada`.

## Segurança
Há `[Authorize]`, antiforgery, isolamento por tenant em leitura/escrita, bloqueio de oficiais, consultas parametrizadas e nenhuma exposição de caminho físico. Eventos de visualização operacional e mutações são cobertos pelo audit de requisição; duplicação, atualização, validação, publicação, versão e teste também são persistidos em `label_template_design_audit`.

## Como validar
1. Aplique `database/apply_all_required_migrations.sql`.
2. Execute clean, restore e build da solução.
3. Abra `/Labels/Designer`, visualize o HOL e confirme bloqueio de edição.
4. Duplique, edite medidas, valide, publique e confira Versions.
5. Confira Preview/PrintTest, PrintWizard, History e as views LocDesk originais.

## Pendências futuras
Drag-and-drop com teclado, snapping configurável, undo/redo, importação de fontes, renderização vetorial/PDF e aprovação em múltiplas etapas ficam para evolução futura.
