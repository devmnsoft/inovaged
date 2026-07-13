# Diagnóstico de estabilização de configuração

Data: 2026-07-13.

## Configuração atual
- `appsettings.json` continha conexão PostgreSQL com usuário `postgres`, senha versionada e valores locais de OCR/preview.
- `SystemSeed` estava habilitado por padrão.
- `Auth:AllowInternalSelfSignedCertificates` estava habilitado.
- `SchemaRepair:Enabled` estava habilitado.
- `App:LocalTimeZoneId` usava `America/Sao_Paulo` enquanto `Localization:DefaultTimeZone` usava `America/Belem`.

## Credenciais encontradas
- `ConnectionStrings:DefaultConnection` continha senha padrão PostgreSQL versionada (valor mascarado no relatório público).
- Seeds continham senhas de demonstração em código.

## Caminhos absolutos
- OCRmyPDF/Python apontavam para perfis `C:\Users\...` e `Administrator`.
- Ferramentas OCR/preview tinham caminhos Windows específicos.

## Configurações perigosas
- Seed ativo por padrão.
- Certificado interno autoassinado permitido.
- Schema repair habilitado por padrão.
- Tenant fixo em workers e agendamentos.
- Usuário operacional `Guid.Empty` em OCR auto schedule.

## Seeds ativos
- `SystemSeedHostedService` executava quando `SystemSeed:Enabled=true` e criava usuários/roles no tenant demo.

## Workers com tenant fixo
- `Workers:LoanOverdue:TenantId`.
- `DocumentQuality:TenantId`.
- `OcrAutoSchedule:TenantId`.

## Divergências de timezone
- Conflito entre `America/Sao_Paulo` e `America/Belem`.
- Persistência já usa vários campos UTC, mas ainda há usos legados de `DateTime.Now` em views e serviços.

## Riscos de produção
- Exposição de senha PostgreSQL.
- Criação de usuários administrativos de demonstração.
- Ambientes subindo com configuração insegura.
- Jobs processando tenant fixo e risco de mistura operacional.

## Arquivos que serão alterados
- Configurações `appsettings`.
- Startup/DI em `Program.cs`.
- Validador de configuração e SystemHealth.
- Seed seguro.
- Serviços de timezone, tenant catalog, locks e usuário de sistema.
- Migrations e documentação.
