# Diagnóstico da regressão visual

A inspeção dos commits de referência identificou que a fundação de UX concentrou melhorias funcionais, mas introduziu um falso fallback de Bootstrap Icons baseado em caracteres Unicode, manteve o login dentro do Razor e deixou componentes críticos responsáveis por decisões de configuração. A correção preserva serviços e regras atuais e separa apresentação, disponibilidade e configuração.

## Causas tratadas
- ausência de compilação Razor obrigatória no build/publish;
- collection expressions recentes em uma view compilada em runtime;
- consulta de feature flag dentro de partial;
- navegação sem persistência, busca e catálogo iconográfico próprio;
- falta de tratamento do schema opcional de continuidade;
- estilos sem uma camada canônica completa.
