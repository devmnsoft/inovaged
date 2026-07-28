# Modelo de sessão

A migration aditiva e idempotente `2026_07_authentication_sessions_and_audit.sql` cria `ged.authentication_session`, com expiração idle e absoluta, revogação, hashes de rede/dispositivo, método, MFA, security stamp, correlation ID e status controlado. Ela não armazena cookie ou token e não revoga sessões existentes.

A persistência e integração desse modelo ao fluxo HTTP permanecem pendentes; a migration isoladamente não ativa gestão de sessões.
