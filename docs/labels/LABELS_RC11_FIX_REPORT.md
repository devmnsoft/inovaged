# Labels RC11 — relatório de correções

## Save do LogoLayout

**Causa:** o editor enviava para uma URL literal e não tornava explícito o token antiforgery; além disso, um acesso acidental por GET à URL de gravação não tinha rota segura.

**Correção:** o formulário agora usa Tag Helpers, POST, rota com `templateCode` e token antiforgery. O GET de `Save` redireciona ao editor com aviso. Falhas de pertencimento/estado da logo voltam ao formulário com validação amigável, em vez de resposta 400 seca.

## History e PostgreSQL 42P08

**Causa:** a consulta comparava parâmetros anuláveis (`@startDate is null`, por exemplo). O PostgreSQL não conseguia inferir o tipo do parâmetro em todas as combinações.

**Correção:** a consulta é montada somente com os filtros informados. Cada valor é adicionado a `DynamicParameters` com `DbType` explícito. Os dados opcionais da evolução RC11 continuam sendo lidos do `snapshot_json`, sem depender de colunas novas.

## Ações de preview e impressão

**Causa:** botões sem submissão HTML confiável dependiam do JavaScript da página.

**Correção:** Pré-visualizar, Visualizar impressão e Imprimir são submits com `formaction` e `formmethod`. As actions recebem o mesmo `LabelPrintWizardInputModel`. A tela final mantém `onclick="window.print()"` como fallback independente do arquivo JavaScript.

## Propagação e renderização da logo

`SelectedLogoAssetId` está no formulário principal e segue pelas actions até o resolver. O resolver produz `PrintImageSource` em Data URI; `_PrintLogo` só cria a imagem quando ela foi carregada e a origem começa por `data:image/`. Assim, uma falha de arquivo gera aviso fora da etiqueta, não um ícone de imagem quebrada.

## Validação manual

1. Abra `/Labels/LogoLayout/LOCDESK_CAIXA_V1`, salve e confirme o retorno ao editor.
2. Acesse diretamente a URL terminada em `/Save` e confirme o redirecionamento com aviso.
3. Abra `/Labels/History` sem filtros e com cada filtro individualmente.
4. No PrintWizard, teste Caixa, Pasta e Pasta HOL; selecione uma logo e confirme o preview visual.
5. Use as três ações e inspecione a etiqueta: a logo real deve ter `src="data:image/..."`.
6. Na página de impressão, clique em **Imprimir agora** e confirme a abertura do diálogo do navegador.

## Melhorias de UX

O wizard mantém configuração e conferência em cards, preview fixo na coluna direita e as três ações juntas. A área da logo explicita origem, ativo selecionado, estado de carregamento, dimensões, proporção, encaixe e posição.

## Pendências

A validação ponta a ponta com dados reais exige tenant autenticado, banco com migrations aplicadas, uma logo ativa e acesso a um navegador gráfico. O quality gate cobre estaticamente o contrato crítico quando esses recursos não estão disponíveis.
