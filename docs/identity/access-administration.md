# Administração de acessos

As inserções de vínculo alteradas em `UserAdminRepository` selecionam usuário e role do mesmo tenant antes de inserir. A trigger é a última barreira contra qualquer outro caminho. A Central de Acessos, matriz transacional, sessões e simulador solicitados não foram implementados neste hotfix e permanecem risco/escopo futuro; nenhuma interface incompleta foi exposta.
