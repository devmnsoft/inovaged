# Arquitetura

O pacote é imutável; PowerShell administra IIS; o Deployment Tool somente inspeciona. Releases versionadas ficam sob `releases`, enquanto configuração, storage, logs e Data Protection ficam em `C:\ProgramData\InovaGed`. Banco é alterado exclusivamente pelo Database Migrator.
