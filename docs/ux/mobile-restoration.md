# Restauração mobile

Abaixo de 992 px a sidebar desktop é substituída por offcanvas de até 320 px. O título permanece na topbar, a ação principal vira ícone acessível em telas estreitas e o conteúdo reduz padding sem criar overflow global. `app-shell.js` fecha o offcanvas após selecionar um link e devolve foco ao botão quando o painel termina de fechar. Reduced motion remove as transições próprias do menu.
