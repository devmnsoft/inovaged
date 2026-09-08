# Guia do usuário — Designer Visual de Etiquetas

## Criar e editar

Abra **Central de Etiquetas → Designer Visual → Novo modelo**. Escolha o tipo (Caixa, Documento, LocDesk, Localização, Classificação, Empréstimo ou Protocolo), informe nome e chave e salve. Insira componentes pela biblioteca esquerda. Arraste no canvas, use o canto inferior direito para redimensionar e refine números no inspetor.

Campos dinâmicos mostram nomes amigáveis. Selecione **Campo dinâmico** e escolha o vínculo; IDs técnicos de banco não são exibidos. Para QR Code, use `Conteúdo do QR Code`. Para localização física, use `Localização`. Logo institucional sem asset válido exibe fallback textual.

## Produtividade

- `Ctrl+S`: salvar rascunho.
- `Delete`: excluir seleção.
- `Ctrl+D`: duplicar seleção.
- `Ctrl+Z` / `Ctrl+Y`: desfazer/refazer.
- Setas: mover 1 mm; `Shift` + setas: mover 5 mm.
- `Shift` + clique: seleção múltipla.

Use a barra flutuante para alinhamento, distribuição, frente/trás e agrupamento. Elementos bloqueados não podem ser arrastados até serem desbloqueados. Grade, régua e zoom afetam somente a edição, não o tamanho impresso.

## Preview, publicação e teste

Salve antes de pré-visualizar. O perfil HOL usa os dados oficiais de amostra, incluindo contrato Hosp. Ophir Loyola, controle 199, classificação, temporalidade, localização e o cabeçalho exato `ARQUIVO LOCDESCK ANANINDEUA`.

Publicar cria um snapshot imutável. Erros críticos precisam ser corrigidos; alertas podem ser aceitos conscientemente. **Testar impressão** abre o mesmo HTML do renderizador real e não substitui o registro de impressão operacional.

## Versões e restauração

Em **Histórico de versões**, consulte número, status, datas, resumo e hash. **Restaurar como rascunho** cria uma cópia editável; a versão publicada original não muda. Para experimentar uma variação, use **Duplicar modelo**.

## Dados reais e solução de problemas

O assistente `/Labels/PrintWizard` resolve o assunto selecionado e envia seus dados ao template publicado. Se um campo ficar vazio, confira se o vínculo pertence ao tipo de assunto. Se o template não aparecer, confirme que está publicado e compatível com Caixa ou Documento. Se a impressão sair deslocada, execute a calibração e confira escala 100%. Se a logo falhar, use um asset institucional válido e revise a área do elemento.
