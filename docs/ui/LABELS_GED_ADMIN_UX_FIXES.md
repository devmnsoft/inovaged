# Labels Print Fix + GED Preview UX Pass

## Problema em `/Labels/Print`
A rota possuía somente uma action POST. Uma navegação GET podia falhar na seleção da action, enquanto payloads incompletos alcançavam consultas de catálogo ou usavam `SubjectId.Value` antes de uma validação defensiva.

## Correção aplicada
- GET `/Labels/Print` agora direciona ao assistente com orientação amigável.
- POST de preview e impressão normaliza e valida modelo, modo, tipo, origem e cópias antes de consultar ou imprimir.
- Reimpressões exigem motivo quando já existe emissão para a origem.
- Falhas de validação retornam ao assistente com modelos e origens recarregados; origem removida ou inválida gera mensagem, não erro 500.
- Os botões usam `type`, `formaction` e `formmethod` explícitos.

## Reformulação de `/Labels/History`
A tela recebeu hero Atlas, atalhos, seis KPIs derivados de dados reais, toolbar, tabela auditável com badges e ações, empty state e orientação de rastreabilidade. Filtros avançados de período, usuário dedicado e status persistido permanecem pendentes de contrato no backend; a pesquisa livre atual já inclui usuário e origem.

## Correção do preview em `/Ged`
O preview é um painel lateral de largura limitada, com rolagem independente e drawer em telas menores. Há controles de fechar no shell e no documento carregado, com suporte a clique e tecla Escape e estado do `body` sincronizado.

## Melhorias de ícones e ações
A listagem mantém ações hierarquizadas de visualizar, classificar, OCR, baixar e abrir detalhes usando o catálogo Atlas existente. Estados de OCR, classificação, documentos incompletos e erros usam badges semânticos.

## Reformulação de `/Administration`
A central executiva usa hero, faixa de ambiente, KPIs reais (ou “Não verificado”), atalhos, grupos administrativos, módulos com ícones e alertas técnicos. O CSS responsivo está isolado em `administration-premium.css`.

## Rotas validadas
O smoke test local cobre `/Labels/Print`, `/Labels/PrintWizard`, `/Labels/History`, `/Ged` e `/Administration`, rejeitando HTTP 500, erros Razor, chave `Title` duplicada e ícones Atlas desconhecidos.

## Pendências restantes
- Aplicar migrations de etiquetas nos ambientes que ainda utilizam catálogo temporário.
- Criar contrato persistido para status de impressão e filtros avançados de período/usuário.
- A geração e o fechamento visual do preview devem ser homologados com dados e autenticação do ambiente alvo.
