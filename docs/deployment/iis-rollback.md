# Rollback IIS

Rollback cobre binários, assets, web.config e physical path, não o banco. Antes da troca, valide checksums e se o schema atual está entre minimumSchemaVersion e maximumCompatibleSchemaVersion. Caso contrário: `ROLLBACK_BLOCKED_SCHEMA_INCOMPATIBLE`.
