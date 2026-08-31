# Labels Print Fidelity 2.0

## Objetivo
Garantir fidelidade entre a prévia e o papel com perfis por impressora, medidas em milímetros e um fluxo de validação que não altera os modelos LocDesk existentes.

## Rotas
`/Labels/Calibration` administra perfis; `/Labels/PrintSheet` prepara folha A4; `/Labels/Batch` mantém o lote; e `/Labels/Quality` executa o checklist. Todos os endpoints pertencem ao controller autorizado de Etiquetas e os POST usam antiforgery.

## Perfis de calibração
Cada perfil pertence ao tenant e registra impressora, papel, orientação, margens, offsets, escala, gaps e observações. Escala aceita 80–120%, offsets -20–20 mm e margens 0–30 mm. Um perfil pode ser padrão.

## Página de teste
Abra **Teste** no perfil. Imprima a página em 100%, sem “ajustar à página”, meça réguas, cantos e a caixa de 100 × 60 mm e corrija X, Y ou escala.

## Prévia de folha A4
A prévia representa 210 × 297 mm, mostra guia de margem, slots, quantidade, gaps, escala e o resumo do perfil aplicado. O modo print remove ferramentas, fundo e sombras.

## Impressão em lote
Em `/Labels/Batch`, selecione modelo, origem e itens pela lista, cópias e perfil. Revise a primeira folha e os alertas antes de imprimir; IDs não são digitados manualmente.

## Aplicação de offset/escala
O CSS compartilha `--label-offset-x-mm`, `--label-offset-y-mm`, `--label-scale`, `--label-margin-top-mm` e `--label-margin-left-mm`. Perfil selecionado prevalece sobre o padrão; sem perfil, os valores são neutros.

## Qualidade visual
A validação cobre borda, fonte, controle, volume, localização, QR, obrigatórios, overflow, dimensão e modo print. Inclui modelos de fábrica, LocDesk e publicados no Designer.

## LocDesk HOL
Preserva borda preta, fundo branco, logo, `ARQUIVO LOCDESCK ANANINDEUA`, contrato Hosp. Ophir Loyola, controle vermelho, volume e LOCALIZAÇÃO.

## LocDesk padrão
`LOCDESK_CAIXA_V1` e `LOCDESK_PASTA_V1` permanecem disponíveis, com controle/volume em vermelho e os campos arquivísticos existentes.

## Integração com PrintWizard
O assistente oferece o perfil padrão ou uma seleção explícita e informa que, sem perfil, será usado o padrão do navegador.

## Integração com Designer
Use **Validar impressão** para abrir `/Labels/Quality`; testes podem empregar o perfil padrão ou o selecionado.

## Como validar impressão real
1. Crie um perfil e torne-o padrão.
2. Imprima a folha de calibração em escala 100%.
3. Meça os quatro cantos e a caixa teste.
4. Ajuste offsets e escala, em passos pequenos.
5. Confira a folha A4 e imprima uma unidade de cada modelo.
6. Leia o QR em dois dispositivos e registre o resultado no checklist.

## Pendências futuras
Detecção automática do driver, captura da área não imprimível, telemetria de leitura de QR e homologação de papéis térmicos dependem de integração nativa com cada estação.
