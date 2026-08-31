# Labels Final UI/UX RC

## Resumo da evolução

Este RC conclui a linguagem visual do módulo de etiquetas sem criar um módulo paralelo. Central, assistente, histórico, LocDesk, Estúdio, Designer, Calibração, lote, QR, scanner e rastreabilidade usam uma hierarquia consistente, microcopy operacional e saídas de impressão isoladas.

## Telas revisadas

Foram revisadas `/Labels`, PrintWizard, History, LocDesk, Templates, Designer, Calibration, Batch, Quality, Quality/QrCode, Scanner, Replacements, Trace e a conferência pública `/l/{token}`.

## Modelos de etiqueta

O catálogo mantém `FACTORY_BOX_V1`, `FACTORY_DOCUMENT_V1`, `LOCDESK_CAIXA_V1`, `LOCDESK_PASTA_V1` e `LOCDESK_PASTA_HOL_V1`, exibindo nome amigável antes do código técnico.

## LocDesk padrão e HOL

Os modelos padrão preservam todos os campos operacionais, QR opcional, borda nítida e controles vermelhos. O HOL continua como documento físico branco, com contrato Hosp. Ophir Loyola e o texto imutável **ARQUIVO LOCDESCK ANANINDEUA**.

## Estúdio e Designer

O Estúdio apresenta miniaturas, tipo, status, versão e caminhos para preview e impressão. Modelos oficiais são somente leitura e duplicáveis; cópias customizadas podem ser editadas, validadas e publicadas sem sobrescrever versões anteriores.

## Calibração e impressão

Perfis guardam margens, offset X/Y, escala e padrão. A página de teste orienta escala 100%. O CSS de impressão usa A4 portrait, fundo branco, remove navegação, ações e sombras, e evita quebra interna da etiqueta.

## QR Code, Scanner e rastreabilidade

O QR usa rota curta sem identificadores sensíveis. Scanner aceita URL, token e TraceCode. A tela pública usa layout independente mobile-first e mostra apenas status, conferência e dados mínimos. A área interna mantém trilha completa e substituições exigem motivo.

## CSS e acessibilidade

Os ativos especializados permanecem separados por responsabilidade, com regras transversais no `labels-premium.css` e impressão no `labels-print.css`. Layouts têm breakpoints, tabelas roláveis, foco visível, labels programáticos, CTAs descritivos e status acompanhados de texto.

## Como validar

1. Execute clean, restore e builds de Application, Infrastructure, Web e solução.
2. Execute Razor/icon checks e route smoke com uma instância autenticável.
3. Percorra as rotas inventariadas no relatório de QA em desktop e mobile.
4. Gere os cinco modelos, confira preview e imprima uma folha a 100%.
5. Gere QR, abra `/l/{token}`, leia no scanner, reimprima com justificativa e crie substituição.

## Pendências futuras

Homologação em cada combinação de impressora, driver e papel; teste assistivo externo; teste touch aprofundado do Designer; paginação server-side do lote em acervos muito grandes.
