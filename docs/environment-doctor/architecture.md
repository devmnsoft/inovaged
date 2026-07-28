# Environment Doctor — arquitetura

Application contém `EnvironmentCheckResult`; Infrastructure fornece verificações reais e o `IModuleReadinessService`; Web continua consumindo prontidão; Environment.Doctor é somente a CLI. O Doctor reutiliza `PostgresModuleReadinessService` para não duplicar a decisão de banco/módulo e nunca corrige o host automaticamente.
