# Relatório da evolução 04.1.24

## Entrega

A evolução introduz artefato framework-dependent `net8.0/win-x64`, manifesto e build information gerados, inventário SHA-256, scanner de segredos, configuração externa, Data Protection compartilhada, publicação IIS, implantação PowerShell separada do banco, smoke tests e documentação operacional.

## Estado de homologação

A implementação não implanta em ambiente real. Testes IIS devem ocorrer apenas no job Windows descartável. Rollback é exclusivamente binário e sempre condicionado à compatibilidade do schema. Riscos restantes: Hosting Bundle/ANCM, certificado, ACL e backup dependem do servidor-alvo e precisam de evidência de homologação antes de retirar o draft.

## Rollback desta evolução

Reverter o commit desta PR; não há migration nova nem alteração automática do banco. Remover apenas artefatos de teste isolados criados pelo job, preservando configuração, storage, logs e Data Protection.
