# Modelo de privacidade e LGPD

- **Proibidos:** CPF, dados médicos, paciente, conteúdo/nome documental, segredo, senha, token, cookie, authorization, connection string, body e query string completa.
- **Permitidos:** nomes canônicos de serviço/operação, rota-template, classe HTTP, versão, cluster/cor controlados e UUID de correlação em trace/log protegido.
- **Mascarados:** IP, caminho interno, host/thumbprint e identificador externo antes da exportação.
- **Agregados:** métricas nunca recebem IDs de usuário/documento/paciente, trace, span ou correlação.
- **Retenção:** backend OTLP define retenção finita; PostgreSQL principal contém apenas SLO, incidentes, alertas, silêncios e markers.
- **Acesso/exportação:** requer permissões específicas e trilha de auditoria. Headers OTLP e credenciais existem somente em configuração externa.
