# Evolução 04.1.38 — auditoria renderizada

| Componente | Problema real | Arquivo responsável | Regra conflitante | Correção realizada | Resultado |
|---|---|---|---|---|---|
| App Shell | Estruturas repetidas e CSS comprimido | `atlas-shell.css` | Sidebar, busca e feedback possuíam definições tardias | Shell reescrito e responsabilidades distribuídas | Estrutura legível, tokenizada e previsível |
| Sidebar | Larguras 280/284px e hover com deslocamento | `atlas-shell.css`, `atlas-navigation.css` | `transform: translateX(2px)` e múltiplas larguras | Propriedade exclusiva de `atlas-navigation.css`, 272/76px, densidades persistidas | Navegação estável em modos confortável, compacto e recolhido |
| Topbar | Repetia título/subtítulo e usava Bootstrap Icons | `_Topbar.cshtml` | Identidade da página misturada às ações globais | Identidade do workspace e Atlas Icons | Barra global orientada a busca e ações autorizadas |
| Context Header | Markup comprimido e breadcrumb fictício | `_ContextHeader.cshtml` | `InovaGED / página` fixo | Modelo tipado, breadcrumb de módulo e identidade Atlas | Contexto separado da topbar e acessível |
| Canvas | Todas as páginas usavam `full` | `_Layout.cshtml` | Classe fixa | Variantes validadas `standard`, `wide`, `full`, `workbench`, `focus` | Densidade adequada ao tipo de tarefa |
| Command Palette | Busca definida junto de overlays | `atlas-overlays.css` | Propriedade CSS ambígua | Movida para `atlas-command-palette.css` | Comando global responsivo e tela cheia no mobile |
| GED | Canvas não respeitava altura útil | `atlas-shell.css`, `atlas-components.css` | Conteúdo com padding global | Workbench usa cálculo por `100dvh` | Área documental preparada para rolagem interna |
| Continuidade | Não havia modo foco ou trilha persistida na sessão | `app-shell.js` | Ausência de estado operacional | F11 interceptado sem fullscreen nativo e API segura de até cinco contextos | Navegação preserva contexto mínimo durante a sessão |
| Mobile | Topbar e overlays mantinham layout desktop | `atlas-responsive.css` | Breakpoints dispersos | Topbar compacta, palette e inspector em tela cheia | Alvos de toque e viewport sem rolagem horizontal global |
| Feedback | Modal, toast e drawer tinham proprietários duplicados | `atlas-components.css`, `atlas-feedback.css` | Estilos repetidos | Elevação concentrada em `atlas-feedback.css` | Feedback com hierarquia consistente |

## Propriedade final

| Componente | Arquivo proprietário |
|---|---|
| App Shell | `atlas-shell.css` |
| Sidebar | `atlas-navigation.css` |
| Topbar | `atlas-shell.css` |
| Context Header | `atlas-shell.css` |
| Command Palette | `atlas-command-palette.css` |
| Componentes | `atlas-components.css` |
| Overlays | `atlas-overlays.css` |
| Feedback | `atlas-feedback.css` |
| Responsividade | `atlas-responsive.css` |
