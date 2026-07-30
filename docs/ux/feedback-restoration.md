# Restauração de feedback

Há um único `appToastContainer` e um único `appConfirmModal`. O servidor publica `Success`, `Info`, `Warning` e `Error` por TempData na fila de feedback. Toasts possuem ícone, fechamento, fila, região `aria-live` e auto-dismiss; erros permanecem até fechamento manual. A confirmação Bootstrap preserva Cancelar, Confirmar, Escape e retorno de foco nativo do modal, sem `window.confirm`.
