# Auditoria tipada

`AuditWriteCommand` transporta os identificadores tipados, correlation ID, tipo do evento, outcome e reason code. O overload posicional foi preservado e marcado como obsoleto. O `AuditWriter` converge ambos os overloads em uma única implementação, grava o correlation ID na coluna de `app_audit_log` e também inclui os metadados no JSON para compatibilidade com `audit_log`.
