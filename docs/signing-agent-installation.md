# Instalação do Signing Agent

O agente deve ser publicado para `win-x64`, instalado por usuário e configurado para escutar apenas em `https://127.0.0.1:17891` e `https://[::1]:17891`. Em produção, gere certificado local exclusivo do agente, instale em `CurrentUser`, confie somente para o usuário atual, registre o thumbprint em configuração local e permita rotação/remoção na desinstalação. Não versione certificado nem chave privada.
