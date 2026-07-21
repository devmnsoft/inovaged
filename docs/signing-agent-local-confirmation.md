# Confirmação local do Signing Agent

A confirmação remota por CORS foi separada da ação local. A página `/operations/{id}/confirm-ui` exibe documento, versão, hash, tamanho, certificado, emissor, CPF mascarado e finalidade; a assinatura é disparada por rota local `confirm-local`.
