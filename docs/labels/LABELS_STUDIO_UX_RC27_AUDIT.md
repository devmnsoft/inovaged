# Auditoria curta — Label Studio RC27

## JÁ FUNCIONA
- Canvas mantém arrastar/soltar, seleção múltipla, redimensionamento, rotação, camadas, agrupamento e atalhos.
- Rascunhos, publicação imutável, revisão, duplicação, versões e restauração já possuem serviços e rotas.
- PrintWizard mantém catálogo, perfis visuais, calibração, impressão e registro auditável.
- Preview operacional do Designer já usa o renderizador real.
- LocDesk/HOL e modelos padrão continuam disponíveis nas rotas existentes.

## POLUIÇÃO VISUAL
- A Central exibe treze cards, incluindo ferramentas administrativas e homologação.
- Toolbar, metadados, identidade visual, Canvas e todas as propriedades competem simultaneamente.
- PrintWizard exibe demonstração, seleção de modo e ajustes milimétricos da logo no fluxo comum.

## DUPLICIDADE
- PrintWizard carrega duas folhas específicas, além de folhas premium e de demonstração.
- Prévia esquemática repete informações da conferência final.
- Ações do Designer aparecem simultaneamente na toolbar, detalhes e lista.

## TERMO TÉCNICO EXPOSTO
- RC3, RC4, quality gate, migration, CANVAS, FACTORY, CUSTOM e LEGACY aparecem em fluxos comuns.
- TemplateKey, SubjectType interno, PaperKind, binding, offsets e formatos internos estão visíveis.

## AÇÃO MAL POSICIONADA
- LocDesk, calibração, demonstração e homologação competem com imprimir.
- Revisão, duplicação, versões e teste competem visualmente com salvar/publicar.
- Configuração do modelo ocupa a área superior do Canvas.

## AJUDA AUSENTE
- Não há guia completo nem ajuda contextual reutilizável.
- Designer não apresenta tour de primeiro uso ou referência de atalhos.
- Histórico não explica claramente reimpressão e QR.

## RISCO DE REGRESSÃO
- Views Razor concentram marcação e expressões dinâmicas sensíveis à compilação.
- Alterar seletores `data-*` quebraria interações já estabilizadas no Canvas.
- Preview não pode registrar impressão, histórico ou TraceCode.
- Derivação de modo deve preservar o campo backend para modelos antigos.
