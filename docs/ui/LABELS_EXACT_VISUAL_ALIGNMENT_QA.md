# Alinhamento visual exato das etiquetas — QA

Este checklist é a fonte de aceite para comparação **lado a lado, em escala 100%**, com os modelos aprovados. A validação de impressão deve usar folha A4, sem cabeçalho/rodapé do navegador, e confirmar as medidas físicas com régua. Dados usados na conferência devem ser fictícios.

| Modelo | Logo correta | Cabeçalho correto | Campos corretos | Ordem correta | Inputs correspondentes | Contraste correto | Preview correto | Impressão correta | Status | Observações |
|---|---|---|---|---|---|---|---|---|---|---|
| `LOCDESK_PASTA_V1` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | Pronto para homologação física | Conferir borda preta, fundo branco, Nº de Controle/Volume e bloco LOCALIZAÇÃO. |
| `LOCDESK_CAIXA_V1` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | Pronto para homologação física | Conferir duas etiquetas por folha e hierarquia específica de caixa. |
| `LOCDESK_PASTA_HOL_V1` | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | Pronto para homologação física | Conferir contrato HOL, controle vermelho e título do arquivo. |
| `FACTORY_BOX_V1` | N/A | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | Pronto para regressão | Identidade InovaGED; não aplicar marca LocDesk. |
| `FACTORY_DOCUMENT_V1` | N/A | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | ☑ | Pronto para regressão | Identidade InovaGED; não aplicar marca LocDesk. |

## Roteiro de conferência

- [ ] Comparar símbolo, lettering **LocDesk**, subtítulo **Inovações Tecnológicas**, cores, proporção e área de respiro com o asset aprovado `wwwroot/images/brands/locdesk/locdesk-logo-original.png`.
- [ ] Confirmar a ordem: Nº de Controle, Volume, Assunto, Detalhamento, Atividade, Classificação, Suporte, Período do Documento, Fase Atual, Previsão Eliminação, Situação Eliminação, Nº LED e LOCALIZAÇÃO.
- [ ] Confirmar que cada campo possui `label` visível e que nenhum identificador técnico é solicitado ao operador.
- [ ] Alternar Pasta, Caixa e HOL e confirmar que o tipo enviado e a prévia correspondem ao modelo selecionado.
- [ ] Validar foco de teclado, mensagens amigáveis, legibilidade de microtextos e contraste de título/subtítulo nos heróis escuros.
- [ ] Abrir `/Labels`, `/Labels/PrintWizard`, `/Labels/LocDesk`, `/Labels/Templates`, `/Labels/History`, `/Labels/Demo` e `/Labels/VisualReview`; confirmar ausência de erro 500.
- [ ] Imprimir em escala 100%; confirmar ausência de sidebar, topbar, botões, sombra e fundo cinza.

## Evidência manual

Registrar navegador, resolução, impressora/perfil, data, responsável, medidas encontradas e divergências. Marcar o modelo como **Homologado** somente após comparar com o PDF original; uma prévia de navegador isolada não encerra a homologação física.
