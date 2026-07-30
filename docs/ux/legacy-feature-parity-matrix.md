# Matriz de paridade visual histórica

SHA auditado: `10d2fd47be068087e0041bba11579349071a399c`.

| Funcionalidade | Origem | Existia | Atual | Funcional | Removida | Restaurar | Evoluir | Evidência |
|---|---|---:|---:|---:|---:|---:|---:|---|
| Shell responsivo | `0aac61b` | Sim | Sim | Sim | Não | Não | Sim | `_Layout.cshtml` e offcanvas mobile |
| Command palette | `0452f81` | Sim | Sim | Sim | Não | Não | Sim | `AppShell/_AppCommandPalette.cshtml` |
| Toasts | `0d05aaf` | Parcial | Sim | Sim | Não | Sim | Sim | fila, limite, pausa e TempData em `inovaged-feedback.js` |
| Confirmação | `0d05aaf` | Parcial | Sim | Sim | Não | Sim | Sim | `InovaGedConfirmDialog`, retorno de foco Bootstrap e digitação opcional |
| Notificações | `0452f81` | Botão | Sim | Sim (estado vazio) | Não | Sim | Sim | drawer acessível no shell; SignalR permanece risco posterior |
| Assistente | `0452f81` | Ação | Sim | Sim (fallback) | Não | Sim | Sim | drawer institucional e encaminhamento para busca autorizada |
| Reduced motion | `0aac61b` | Parcial | Sim | Sim | Não | Sim | Sim | media query e preferência por atributo |
| Empty states ilustrados | — | Não | Sim | Sim | Não | Sim | Sim | catálogo local `images/illustrations` |

O histórico foi usado apenas como referência. Nenhum controller, regra de autorização ou serviço de negócio foi revertido.
