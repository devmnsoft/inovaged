# Simplificação do shell

`_Layout.cshtml` passou a ser um composition root: assets, sidebar, navegação mobile, topbar, conteúdo, feedback e scripts. O `UserShellContextService` é responsável por perfil prioritário, label, setor, iniciais, aviso, menu permitido e ação principal. Os partials recebem records imutáveis (`AppShellVM`, `AppUserShellVM`, seções, itens e ação).

Paleta de comandos, notificações, assistente, drawers de experiência e badge de classificação deixaram o fluxo de renderização. Os módulos não foram apagados. As flags `ShellFeatures` ficam desativadas em todos os appsettings versionados.
