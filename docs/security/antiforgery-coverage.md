# Cobertura antiforgery

Forms Razor existentes usam tokens MVC em diversos fluxos, inclusive logout. Ações JSON autenticadas por cookie, movimentação, classificação rápida, exportação e sessões de assinatura precisam de testes de integração para token ausente, inválido e válido. A proteção não foi desabilitada globalmente e esta documentação não promove cobertura não executada.
