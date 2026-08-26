# LocDesk — Pasta HOL

## Objetivo
O `LOCDESK_PASTA_HOL_V1` reproduz a etiqueta arquivística de pasta/documento do Hospital Ophir Loyola (HOL), preservando contrato, controle, volume, temporalidade e localização.

## Diferença entre os modelos
- **LocDesk padrão (`LOCDESK_PASTA_V1`)**: layout Ananindeua com QR Code e campos operacionais existentes.
- **LocDesk HOL (`LOCDESK_PASTA_HOL_V1`)**: layout exclusivo do Hospital Ophir Loyola, sem substituir o padrão.
- **LocDesk caixa (`LOCDESK_CAIXA_V1`)**: identificação de caixas e impressão em grade A4.

## Campos
Contrato, prontuário, controle, volumes, assunto, detalhamento, atividade, classificação, suporte, período inicial/final, fase atual, previsão e situação de eliminação, LED e localização. Contrato, controle, assunto, atividade, classificação e suporte são obrigatórios; volumes e datas têm validação cruzada.

## Como imprimir
Abra **Etiquetas > LocDesk**, escolha **LocDesk Pasta HOL**, confira a pré-visualização e use **Gerar impressão**. Na página de impressão, mantenha escala de 100%; menus, ações, sombras e fundo externo são removidos automaticamente.

## Como validar
1. Em `/Labels/LocDesk`, alterne entre os três modelos e confira que o HOL mantém borda preta, contrato destacado e controle vermelho.
2. Em `/Labels/PrintWizard`, use `DOCUMENT` + `CUSTOM` e selecione **LocDesk - Pasta HOL**.
3. Confira `/Labels/History` e o badge **LocDesk HOL** após uma impressão.
4. Execute o build da solução e o smoke test autenticado das rotas de Labels.

## Arquivos alterados
View HOL, formulário LocDesk, controller e models de Labels, catálogo mínimo, migration, CSS dedicado, History e smoke test.

## Pendências
A conferência milimétrica final depende de prova física na impressora e no papel usados pelo cliente. O filtro de status do History representa hoje os modos disponíveis; estados de falha exigem persistência no backend antes de poderem ser consultados.
