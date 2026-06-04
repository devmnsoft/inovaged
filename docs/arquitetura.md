# Arquitetura do InovaGED

O InovaGED usa ASP.NET Core MVC, Razor, C#, Dapper e PostgreSQL. A solução separa domínio, aplicação, infraestrutura, web e testes automatizados.

## Camadas

- **InovaGed.Domain**: entidades, enums e objetos centrais.
- **InovaGed.Application**: contratos, DTOs, serviços de aplicação e regras orquestradas.
- **InovaGed.Infrastructure**: repositórios Dapper, consultas SQL, workers e integrações.
- **InovaGed.Web**: controllers MVC, views Razor, JavaScript, autenticação e composição de DI.
- **database**: migrations, diagnósticos e otimizações SQL.

## Fluxos principais

1. Usuário autentica e recebe perfil/permissões.
2. Controller valida autorização e chama serviços de aplicação.
3. Repositórios Dapper executam consultas no schema `ged`.
4. Arquivos são gravados em storage operacional.
5. OCR e preview são enfileirados e processados por workers.
6. Logs e auditoria registram eventos com correlationId quando disponível.

## Decisões operacionais

- Indicadores dependem de consultas reais no banco.
- Upload registra batch e item antes do processamento assíncrono.
- OCR/preview nunca devem bloquear desnecessariamente a resposta de upload.
- Menus e rotas são protegidos por perfil e controller.
