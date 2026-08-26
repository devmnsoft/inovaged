# Administration Executive Redesign

## Correção do PrintWizard

A falha HTTP 500 de `/Labels/PrintWizard` era causada pela tentativa de adicionar novamente a chave `Title` a uma cópia de `ViewData` que já continha o título da página. O wizard agora prepara os dicionários do stepper e do alerta no bloco Razor, usa atribuição por indexador e envia o título específico `AlertTitle` ao alerta.

Os componentes Atlas revisados passaram a usar chaves semânticas (`AlertTitle`, `HeroTitle`, `CardTitle`, `KpiTitle`, `TableTitle` e `StepperTitle`) em vez de disputar o título da página. Os demais usos inline encontrados em Etiquetas e no catálogo do Design System também foram migrados para chaves específicas.

## Central Administrativa

A página `/Administration` foi reorganizada como uma central executiva com:

1. hero institucional e cinco atalhos;
2. faixa de status baseada nas métricas reais do dashboard;
3. oito KPIs, com `Não verificado` quando não existe telemetria;
4. ações rápidas;
5. módulos agrupados por governança, banco e ambiente, observabilidade, inteligência, entrega e configurações;
6. painel de alertas técnicos alimentado pelas recomendações existentes;
7. orientação operacional no rodapé.

O CSS `administration-premium.css` implementa a composição responsiva com os tokens Atlas de cor, superfície, borda, sombra e raio. A interface usa `app-icon`, badges de estado e os partials de recomendações existentes; nenhuma informação operacional fictícia foi adicionada.

## Rotas de validação

O smoke test deve cobrir:

- `/Labels/PrintWizard`
- `/Administration`
- `/Administration/Security`
- `/Administration/Users`
- `/Administration/Migrations`
- `/Administration/DesignSystem`
- `/DatabaseReadiness`
- `/SystemIncidents`
- `/SchemaHealth`

## Pendências

Métricas ainda não disponibilizadas pelo serviço administrativo aparecem explicitamente como **Não verificado**. A validação autenticada depende de banco configurado, usuário administrativo e dados do ambiente de execução.
