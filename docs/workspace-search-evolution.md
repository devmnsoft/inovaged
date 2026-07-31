# Busca global e evolução segura do workspace

## Base e correção Razor

A evolução partiu do commit `3095c72`. O erro RZ2005/RZ1011 ocorria porque `section`, palavra reservada do Razor, era usada como variável e depois referenciada como `@section` em um loop comprimido. A view agora usa índices determinísticos e os nomes explícitos `menuGroup` e `menuItem`.

## Arquitetura

A busca global combina imediatamente a navegação já autorizada do App Shell com uma consulta remota. O endpoint autenticado exige o claim `tenant_id`, limita consultas a 20 itens e delega a provedores isolados. O provedor documental reutiliza `ISmartSearchService`, com escopo de tenant e usuário e no máximo cinco documentos. Falhas de um provedor são registradas sem indisponibilizar os demais.

Navegação não é consultada novamente no banco. Os provedores de protocolos, empréstimos e usuários permanecem sem resultados até que consultas de aplicação com escopo e políticas equivalentes estejam disponíveis; isso evita contornar autorizações existentes ou revelar dados por SQL paralelo.

## Front-end e segurança

`global-search.js` implementa debounce, cancelamento, estados, atalhos, retorno de foco e navegação por teclado. Resultados são construídos com DOM seguro e URLs externas ou esquemas perigosos são descartados. O painel é um dialog não modal responsivo, sem focus trap.

O menu Criar recebe somente ações tipadas montadas pelo `UserShellContextService` conforme o perfil. O partial não contém rotas de negócio hardcoded.

## Operação e rollback

Para rollback, reverta os commits desta branch na ordem inversa. Não há migração de banco, novo índice ou alteração em regras de upload/OCR. Dashboard, GED e feedback já usam dados e componentes reais e foram preservados para evitar uma reconstrução de alto risco neste hotfix.
