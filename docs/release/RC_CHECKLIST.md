# Release Candidate Readiness — checklist

Use `PENDENTE`, `EM VALIDAÇÃO`, `APROVADO` ou `BLOQUEADO` no campo **Status**. Nenhum item bloqueante pode ser aprovado sem evidência. O responsável padrão é o líder técnico da homologação, que deve registrar o nome na execução.

| Área | Status | Como validar | Comando ou rota | Responsável | Observação |
|---|---|---|---|---|---|
| Build | PENDENTE | Compilar sem erro ou warning novo | `dotnet build InovaGed.sln -v:minimal` | Líder técnico | Anexar log |
| Migrations | PENDENTE | Confirmar zero migration obrigatória pendente | `/DatabaseReadiness` | DBA | Não aplicar sem backup |
| Seeds | PENDENTE | Executar duas vezes sem duplicar nem alterar dados | `psql -f database/seeds/2026_08_release_candidate_demo_seed.sql` | DBA | Apenas demo/homologação |
| DI | PENDENTE | Validar grafo crítico | `dotnet run --project InovaGed.Environment.Doctor -- di-check` | Backend | Zero falha crítica |
| Dapper | PENDENTE | Auditar aliases, nullables e materialização | `dotnet run --project InovaGed.Environment.Doctor -- dapper-mapping` | Backend | Zero risco crítico |
| Razor | PENDENTE | Compilar views e verificar partials | `dotnet run --project InovaGed.Environment.Doctor -- razor-check` | Frontend | Zero erro conhecido |
| Rotas | PENDENTE | Executar smoke; nenhuma resposta 500 | `dotnet run --project InovaGed.Environment.Doctor -- route-smoke` | QA | 401/403 podem ser esperados |
| Menus | PENDENTE | Abrir links por perfil autorizado | `dotnet run --project InovaGed.Environment.Doctor -- admin-links-check` | QA | Incompletos ficam desabilitados |
| Ícones | PENDENTE | Validar catálogo e aliases | `dotnet run --project InovaGed.Environment.Doctor -- icon-check` | Frontend | Zero ícone desconhecido |
| Administração | PENDENTE | Conferir KPIs, cards e permissões | `/Administration` | Produto | Sem link quebrado |
| Etiquetas | PENDENTE | Abrir wizard, histórico e LocDesk | `/Labels/PrintWizard` | QA | Impressão controlada |
| LocDesk | PENDENTE | Validar indisponibilidade amigável | `/Labels/LocDesk` | Operações | Não expor segredo |
| Retenção | PENDENTE | Abrir destinação e conferir empty state | `/RetentionDestination` | Arquivista | Sem decisão automática |
| Instrumentos | PENDENTE | Abrir versão PCD | `/Instruments/Versions/PCD` | Arquivista | Conferir publicação |
| Acervo físico | PENDENTE | Abrir caixas e isolamento do tenant | `/Physical/Boxes` | Arquivista | Sem dados cruzados |
| Incidentes | PENDENTE | Confirmar ausência de crítico aberto | `/SystemIncidents` | SRE | Crítico bloqueia RC |
| SchemaHealth | PENDENTE | Executar diagnóstico seguro | `/SchemaHealth` | DBA | Sem SQL do usuário |
| DatabaseReadiness | PENDENTE | Validar plano e checksums | `/DatabaseReadiness` | DBA | Aplicação exige autorização |
| Segurança | PENDENTE | Testar autorização, antiforgery e segredo | `/Security/Roles` | Segurança | Admin recebe permissões RC |
| LGPD | PENDENTE | Revisar minimização, retenção e auditoria | `/Administration` | DPO | Seed não contém dado pessoal real |
| Backup | PENDENTE | Registrar backup e teste de restauração | `/Continuity/Overview` | SRE | Evidenciar RPO/RTO |
| Homologação | PENDENTE | Executar relatório final e aceite | `dotnet run --project InovaGed.Environment.Doctor -- release-readiness` | Produto/QA | Guardar artifacts/release |

## Critérios bloqueantes

- Rota crítica com HTTP 500, DI crítico ausente, migration obrigatória pendente, incidente crítico aberto, risco crítico Dapper ou erro Razor conhecido bloqueiam a promoção.
- O relatório não contém connection string, senha, token, stack trace ou SQL fornecido pelo usuário.
