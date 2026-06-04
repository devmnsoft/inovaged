# Perfis e Permissões

## Perfis principais

- **ADMIN**: administração completa.
- **ADMINISTRADOROPHIR**: HospitalDocuments, solicitações quando aplicável e saída.
- **ARQUIVISTAOPHIR**: operações arquivísticas autorizadas.
- **Usuário hospitalar**: busca hospitalar e visualização conforme permissão.

## Regras

- Menus variam por permissão.
- Controllers protegem rotas independentemente da visibilidade no menu.
- Operações sensíveis devem registrar auditoria.
- Acesso a documento confidencial exige validação específica.
