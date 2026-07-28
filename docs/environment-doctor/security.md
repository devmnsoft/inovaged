# Segurança do Doctor

A saída contém somente metadados allowlisted. Caminhos são reduzidos ao último segmento; versão do SDK incompatível é reduzida ao major. Connection strings, senhas, tokens, cookies, chaves, certificados privados, CPF, dados médicos, conteúdo de appsettings e variáveis arbitrárias não são impressos. Relatórios ficam em `artifacts/environment-doctor`, ignorado pelo Git.
