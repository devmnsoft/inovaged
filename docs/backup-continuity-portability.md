# Backup, Continuidade e Portabilidade

Módulo aditivo na área administrativa existente, protegido por `AppPolicies.SystemAdmin`. A Evolução 01 foi detectada por `docs/guia-administrador-administracao.md` e pela migration `2026_07_administration_security_governance.sql`; por isso não foi criado um menu administrativo concorrente.

## Componentes
- Application: contratos `IBackupPolicyService`, `IBackupOrchestrator`, `IBackupCatalogService`, `IBackupIntegrityService`, `IRestoreValidationService`, `IRecoveryPlanService`, `IPortabilityExportService`, `IPortabilityManifestService`, `IPortabilityPackageVerifier`, `ITenantOffboardingService`, `IDataDeletionWorkflowService` e `IRecoveryObjectiveService`.
- Infrastructure: repositório Dapper, provider `PostgresBackupProvider`, verificador de pacote e validação de restore isolado.
- Worker opcional: `InovaGed.Operations.Worker`, desabilitado por padrão.
- Verificador independente: `InovaGed.Portability.Verifier`.

## Tabelas
`backup_policy`, `backup_job`, `backup_set`, `backup_artifact`, `backup_verification`, `restore_test`, `restore_test_check`, `recovery_plan`, `recovery_plan_version`, `recovery_test`, `recovery_objective_measurement`, `portability_export`, `portability_export_item`, `portability_artifact`, `tenant_offboarding`, `tenant_offboarding_event`, `data_retention_hold`, `operations_worker_heartbeat` e `operations_dead_letter`.

## Segurança e limitações
Backups e exportações dependem de configuração explícita. Senhas não são passadas na linha de comando do `pg_dump`; usa-se PGPASSFILE temporário. Não há failover automático, WAL/PITR completo, storage geográfico, restore em produção ou exclusão física automática de tenant nesta evolução.
