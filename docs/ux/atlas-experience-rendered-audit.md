# Auditoria da experiência Atlas renderizada — evolução 04.1.34

## Escopo e método

Baseline: `8a58d70d9bde8d92e675de92f5cfb9b44b7e25c1`. A inspeção estática foi feita no shell, login, dashboard, GED, busca, upload, preview, protocolos, empréstimos e administração. A homologação renderizada permanece pendente porque o SDK .NET não está disponível no ambiente de execução; por isso este documento não declara evidência visual inexistente.

## Diagnóstico por superfície

| Superfície | Problema visual/funcional encontrado | Correção 04.1.34 | Homologação manual |
|---|---|---|---|
| Login | O sprite era incluído diretamente e os assets não tinham ponto único de composição. | Asset parcial comum no layout autenticado e de login. | Pendente em 390, 768, 1440 e 1920 px. |
| Sidebar e topbar | Risco de fallback vazio quando o nome não existe e atributos do chamador inconsistentes. | Fallback visível, classes mescladas e contrato acessível. | Conferir expandida/recolhida e foco. |
| Dashboard | Dependência do mesmo contrato de ícones das filas operacionais. | Registry e variantes normalizados. | Conferir perfis administrador, arquivista e hospital. |
| GED | Ícones semanticamente distintos apontavam para geometria documental repetida. | Inventário registra a dívida visual; símbolos críticos devem ser homologados individualmente. | Conferir tabela, lista, cards e três painéis. |
| Upload Center | Feedback depende de ícone e ilustração registrados. | Registry fechado para ilustrações e resolução central de tipos de arquivo. | Conferir fila, minimizado, pausa e duplicidade. |
| Preview | Estados possuem assets separados, mas dimensões eram impostas pelo tamanho genérico. | Registry passa a expor dimensões e uso do asset. | Conferir vazio, carregando, incompatível, restrito e erro. |
| Busca e assistente | Fontes e resultados precisam de semântica consistente de arquivo. | Resolver único de visual por extensão/MIME. | Conferir fontes reais e ausência de painel cenográfico. |
| Protocolos, empréstimos e administração | Bootstrap legado ainda pode existir fora das superfícies migradas. | Classificado como legado, sem alegar migração completa. | Validar rotas, 404, HTTP 500 e console. |
| Mobile | Alvos e drawers dependem da folha Atlas existente. | Nenhuma nova camada visual paralela foi criada. | Conferir 390 × 844 e 768 × 1024. |

## Checklist de inspeção humana

Em cada superfície, registrar: ícone quebrado/inadequado, imagem inadequada, mensagem técnica, ação concorrente, botão sem função, excesso de cards, hierarquia, feedback, responsividade, assets 404, `console.error` e HTTP 500. Capturas antes/depois devem ser produzidas fora deste ambiente nas resoluções requeridas.
