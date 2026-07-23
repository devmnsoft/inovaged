# Signing Agent Local HTTPS Installation

O comando `install` deve gerar certificado HTTPS de loopback, instalar em `CurrentUser`, configurar confiança local, salvar thumbprint protegido e registrar inicialização do usuário. `rotate-certificate` deve trocar somente certificados criados pelo agente após validação. `uninstall` deve remover inicialização, revogar pairings e preservar certificados pessoais.
