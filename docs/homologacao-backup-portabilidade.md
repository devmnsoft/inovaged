# Homologação — Backup e Portabilidade

## Checklist

- Solicitar backup manual por tenant.
- Confirmar criação de `backup_set`.
- Confirmar `database.dump`, `manifest.json` e `checksums.sha256`.
- Executar `pg_restore --list database.dump`.
- Validar que manifesto/download de outro tenant retorna 404.
- Solicitar exportação com a mesma `Idempotency-Key` duas vezes e confirmar mesmo registro.
- Garantir que pacote de portabilidade não contenha senha, token, cookie, segredo JWT ou connection string.
