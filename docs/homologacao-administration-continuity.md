# Homologação Administração e Continuidade

1. Aplicar migrations em banco limpo e reaplicar para comprovar idempotência.
2. Acessar painel administrativo como administrador local e confirmar que apenas o tenant da identidade é exibido.
3. Acessar como administrador global e validar filtro explícito por tenant.
4. Ativar `AUDIT_ONLY` em tenant de homologação e comparar divergências registradas em `ged.permission_evaluation_log`.
5. Solicitar backup com `Backup:Enabled=true` apenas em homologação.
6. Verificar jobs em `ged.backup_job` com lease, status e progresso.
7. Validar pacote de portabilidade com `InovaGed.Portability.Verifier`.
