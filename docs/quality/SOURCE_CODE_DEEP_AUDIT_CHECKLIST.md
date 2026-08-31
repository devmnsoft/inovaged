# Checklist — Source Code Deep Audit

- [x] **Application:** contratos e modelos novos sem dependência de infraestrutura.
- [x] **Infrastructure:** auditoria de consistência schema-aware e mapeamento manual corrigido.
- [x] **Web:** rotas, autorização, tenant claim, logging e Atlas UI revisados.
- [x] **Doctor:** manifesto de rotas atualizado.
- [x] **Workers:** inventariados; nenhuma mudança de execução necessária.
- [x] **Migrations:** manifesto/agregador e padrões destrutivos revisados estaticamente.
- [x] **Views:** telas novas tipadas; CSS externo; submit explícito.
- [x] **CSS/JS:** CSS responsivo isolado; nenhum JS necessário para fluxos GET.
- [x] **DI:** serviço novo registrado como scoped.
- [x] **Dapper:** materialização perigosa de consistência de upload corrigida.
- [x] **Razor:** foreach inline removido do dashboard alterado.
- [x] **Schema:** consultas opcionais verificam tabela/coluna.
- [x] **Business Rules:** diagnóstico somente leitura; valores ausentes não são falsificados.
- [x] **Security:** controllers autorizados; nenhum SQL livre vindo do usuário.
- [x] **Tenant Isolation:** busca/contexto e checks usam tenant autenticado.
- [ ] **Build:** bloqueado — `dotnet` ausente no contêiner.
- [ ] **Git sync:** registrar resultado final após commit/pull.
