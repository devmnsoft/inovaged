# Relatório de estabilização de configuração

Data: 2026-07-13.

## Arquivos alterados
- `InovaGed.Web/appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json`, `appsettings.Example.json`.
- `InovaGed.Web/Program.cs`.
- `InovaGed.Web/Controller/SystemHealthController.cs` e views de SystemHealth.
- `InovaGed.Application/SystemHealth/StartupConfigurationHealth.cs`.
- Serviços em `InovaGed.Infrastructure/SystemHealth`, `Common/Time`, `Tenants`, `Jobs`.
- `InovaGed.Infrastructure/Setup/SystemSeedHostedService.cs`.
- `database/migrations/2026_07_estabilizacao_configuracao.sql`.
- Testes em `InovaGed.Application.Tests/StartupConfigurationValidatorTests.cs`.
- Documentação de configuração, segurança, workers e homologação.

## Problemas corrigidos
- Removida senha PostgreSQL versionada.
- Removidos caminhos pessoais obrigatórios de OCR/preview.
- Seed desabilitado por padrão e bloqueado fora de Development/PoC.
- Certificado interno autoassinado desabilitado por padrão.
- Timezone padrão unificado para `America/Belem`.
- Introduzidos contratos para tenants ativos, contexto de execução, lock e usuário de sistema.
- Criadas telas SystemHealth de configuração segura e workers.

## Testes criados
- String de conexão ausente.
- Senha padrão em Production.
- Seed em Production.
- Certificado autoassinado em Production.
- Timezone padrão e por tenant.
- Conversão UTC para America/Belem.
- Mascaramento de segredos.

## Resultado do build e testes
O ambiente atual não possui `dotnet` instalado (`/bin/bash: dotnet: command not found`). Build e testes devem ser executados no agente com SDK .NET 8.

## Riscos restantes
- Ainda existem usos legados de `DateTime.Now` em views e trechos não críticos.
- Nem todos os workers foram reescritos; a base de contratos e locks foi criada para migração incremental.
- O painel de workers depende da migration aplicada e dos workers persistirem estado real.

## Implantação
1. Aplicar `database/migrations/2026_07_estabilizacao_configuracao.sql`.
2. Configurar `ConnectionStrings__DefaultConnection` no ambiente.
3. Configurar storage e dependências OCR/preview por ambiente.
4. Subir aplicação e validar `/SystemHealth/SecurityConfiguration`.

## Rollback
1. Reverter commit da aplicação.
2. Opcionalmente manter tabelas novas, pois são aditivas.
3. Remover variáveis novas somente após confirmar rollback.

## Próxima etapa
Migrar cada worker real para `ITenantCatalog` + `IJobExecutionLock` e persistência em `ged.worker_execution_state`.
