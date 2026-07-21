# Homologação de Continuidade e Portabilidade

1. Aplicar migration em PostgreSQL de homologação.
2. Executar `dotnet build` e testes.
3. Validar menu `/Continuity` com usuário admin e 403 com usuário comum.
4. Solicitar backup, iniciar worker e confirmar job persistido.
5. Validar manifesto, checksums e verificador independente.
6. Executar restore isolado com `pg_restore` em destino allowlist.
7. Confirmar que exportação/importação/download legados permanecem acessíveis.
