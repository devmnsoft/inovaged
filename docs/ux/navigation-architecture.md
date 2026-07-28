# Arquitetura de navegação

## Estado entregue

O destino administrativo canônico é `/GedDashboard`, separado de `/Ged`. O shell mantém um único logout e aplica estado ativo ao controller real do dashboard.

## Débito controlado

`_SidebarMenu.cshtml` ainda decide visibilidade por perfis estáticos. A próxima fatia deve introduzir `IApplicationNavigationService`, avaliar policy/permissão/tenant/módulo/feature/setor no servidor, e produzir grupos tipados. Até isso ocorrer, o menu não deve ser descrito como permission-driven completo.
