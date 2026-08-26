# Atlas UX Forms Pass — Validações e Campos Inteligentes

## Escopo desta rodada

A rodada consolida a base reutilizável de formulários Atlas e prioriza o fluxo `/Labels/PrintWizard`, que deixou de solicitar um `Guid` digitado. A origem agora é escolhida por nome/código em uma lista limitada a caixas, documentos ou lotes ativos do tenant autenticado.

## Telas revisadas

- **`/Labels/PrintWizard`**: seleção guiada da origem e do modelo, quantidade limitada, mensagens por campo, ajuda para reimpressão, prévia/resumo e estado de geração.
- **`/Labels/LocDesk`**: auditada; os vínculos técnicos continuam ocultos e são preenchidos ao entrar pelo assistente/listagens de caixas ou documentos. O cabeçalho `ARQUIVO LOCDESCK ANANINDEUA` foi preservado e a regra `volume atual <= volume total` já é validada no servidor.
- **`/Labels/Boxes`, `/Labels/Documents`, `/Physical/Boxes`**: auditadas como pontos de seleção/navegação para os itens, sem novo campo técnico editável.
- **Administração, Retenção, Instrumentos, SystemIncidents e UAT**: inventariados. Não foram alterados nesta fatia para evitar mudança de contratos e regras sem os catálogos próprios de cada módulo.

## Campos por ID eliminados

- `SubjectId` no assistente de etiquetas deixou de ser caixa de texto. O valor é enviado por `<select>` e suas opções são obtidas no servidor com isolamento por `tenant_id` e apenas registros ativos.
- `BoxId` e `DocumentId` do LocDesk permanecem campos internos ocultos; o operador os escolhe pelo nome/código no assistente ou nas listagens, nunca digitando UUID.

## Dropdowns e autocompletes

- Criado dropdown de origem para caixas, documentos e lotes, limitado a 200 resultados e ordenado por um identificador operacional amigável.
- Criado dropdown de modelos com opção inicial explícita e validação associada.
- Foi criada a partial de dica de autocomplete para adoção pelos módulos com catálogos grandes. Nenhum endpoint de autocomplete foi criado: para a central de etiquetas, o dropdown limitado evita uma API adicional e mantém a consulta tenant-safe.

## Componentes Atlas consolidados

Foram disponibilizados componentes para seção, campo, select, radio, checkbox, resumo de validação, ações e dica de autocomplete em `Views/Shared/Atlas`. Eles oferecem label, ajuda, placeholder, validação associada, estados disabled/read-only e empty state.

O script global `wwwroot/js/atlas-forms.js` evita envio duplo de formulários POST válidos, desabilita os botões e comunica `Salvando...`, `Gerando...` ou o texto definido em `data-loading-text`.

## Validações e mensagens

- Origem obrigatória, salvo etiqueta manual; tipo e modo validados no servidor.
- Compatibilidade do modelo validada no servidor.
- Quantidade entre 1 e 500 no cliente e no `DataAnnotations` do servidor.
- Existência e isolamento do item permanecem garantidos pelas consultas do controller com `tenant_id`; item inexistente não é impresso.
- Reimpressão sem justificativa continua rejeitada pelo registrador e agora apresenta mensagem junto ao campo, preservando os valores e as listas do formulário.
- O resumo de validação recebe foco quando renderizado com erros.

## Pendências restantes

A aplicação é extensa e ainda há entradas técnicas fora das telas de etiquetas (por exemplo, vínculos antigos em UAT, protocolo e módulos hospitalares). A conversão exige ViewModels e catálogos tenant-safe específicos e deve ocorrer em fatias próprias. A rota `/PostGoLive` não possui controller/view neste checkout. As rotas de Instrumentos existentes usam nomes de actions/views diferentes de `/Instruments/Versions/PCD|TTD|POP`; foram documentadas, não recriadas, para não introduzir rotas ou módulos artificiais.

## Como validar

1. Abrir `/Labels/PrintWizard` e alternar entre Caixa, Documento/Pasta e Lote.
2. Confirmar que a origem é uma lista com nome/código e que UUID não pode ser digitado.
3. Enviar vazio e conferir resumo e mensagens associadas aos campos.
4. Selecionar origem/modelo, informar ao menos uma cópia e gerar a prévia.
5. Imprimir um item já impresso sem justificativa e confirmar a mensagem; repetir com justificativa.
6. Confirmar que o botão muda para `Gerando...` e não aceita segundo envio.
7. Acessar o fluxo LocDesk por uma caixa/documento e confirmar vínculo e preview.
8. Executar os comandos de clean, restore e build da solução descritos no pedido.

> Limitação do ambiente desta execução: o SDK `dotnet` não está instalado no container, portanto os comandos obrigatórios foram tentados, mas não puderam ser executados aqui.
