# Avaliação de permissões

`DatabasePermissionChecker` percorre usuário, relação de roles, role do mesmo tenant, `role_permission` ativa por `reg_status = 'A'` e permission por código. Não presume IDs/flags inexistentes e não concede bypass por coluna administrativa. A consolidação ampla em `IAccessDecisionService`, cache versionado e ABAC permanece para uma evolução posterior; não é alegada como concluída neste hotfix.
