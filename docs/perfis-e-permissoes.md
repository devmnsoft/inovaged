# Perfis e permissões do InovaGED

Este documento consolida a matriz oficial de acesso do InovaGED. A segurança deve ser aplicada em policies, controllers, endpoints AJAX e escopos de consulta; o menu lateral é apenas uma camada de UX.

## Perfis oficiais

- **ADMIN**: administrador global, com acesso total a telas, ações administrativas, GED, documentos hospitalares, empréstimos, protocolo, parâmetros, usuários, logs, schema health, homologação e dashboards.
- **ADMINISTRADOR**: administrador global equivalente ao ADMIN, com o mesmo acesso total.
- **ADMINISTRADOROPHIR**: perfil operacional de gestão Ophir. Acessa documentos hospitalares, filas operacionais, manuseio/processamento de protocolos do seu setor e gestão de empréstimos do seu setor. Não acessa administração global.
- **ARQUIVISTAOPHIR**: perfil operacional de solicitação e acompanhamento. Acessa documentos hospitalares, solicitação de protocolo, minhas solicitações, criação de solicitação de documento/empréstimo e acompanhamento dos próprios pedidos. Não executa ações de aprovação, entrega ou devolução.
- **HOSPITAL**: perfil hospitalar/leitura. Acessa apenas a busca hospitalar/documentos permitidos, sem ações administrativas.

## Redirecionamento pós-login

- **ADMIN / ADMINISTRADOR**: `/Ged` por padrão, respeitando `returnUrl` local válido.
- **ADMINISTRADOROPHIR**: `/Operations`; se acessar um `returnUrl` operacional permitido, ele é respeitado.
- **ARQUIVISTAOPHIR**: `/ProtocolRequests`; se acessar um `returnUrl` operacional permitido, ele é respeitado.
- **HOSPITAL**: `/HospitalDocuments`.

## Menus visíveis

### ADMIN e ADMINISTRADOR

Exibem todos os menus administrativos e operacionais: Dashboard, Painel GED, Inteligência Hospitalar, Alertas e Tendências, System Health, Schema do Banco, Homologação, Explorer de Documentos, Busca Hospitalar, Criar Documento, Pastas e Dossiês, OCR, KPI do GED, lotes, importação/exportação, sistema de imagem, guarda física, empréstimos, protocolo, parâmetros, usuários, logs e configurações.

### ADMINISTRADOROPHIR

- Buscar Prontuários e Documentos
- Central Operacional
- Fila de Protocolos / Manuseio de Processos
- Loans/Empréstimos para aprovação e tratamento
- Solicitações de Documentos
- Sair

### ARQUIVISTAOPHIR

- Buscar Prontuários e Documentos
- Solicitar Protocolo
- Minhas Solicitações de Protocolo
- Solicitar Documento/Empréstimo
- Meus Pedidos
- Sair

### HOSPITAL

- Buscar Prontuários e Documentos
- Sair

## Matriz de permissões

| Funcionalidade | ADMIN | ADMINISTRADOR | ADMINISTRADOROPHIR | ARQUIVISTAOPHIR | HOSPITAL |
|---|---:|---:|---:|---:|---:|
| GED administrativo | Total | Total | Operacional | Operacional | Não |
| HospitalDocuments | Total | Total | Sim | Sim | Leitura |
| Loans - visualizar | Todos | Todos | Setor | Próprios | Não |
| Loans - criar | Sim | Sim | Sim | Sim | Não |
| Loans - aprovar/rejeitar | Sim | Sim | Setor | Não | Não |
| Loans - entregar/devolver | Sim | Sim | Setor | Não | Não |
| Protocolo - solicitar | Sim | Sim | Sim | Sim | Não |
| Protocolo - manusear fila | Sim | Sim | Setor | Não | Não |
| Operações | Total | Total | Sim | Sim | Não |
| Parâmetros | Sim | Sim | Não | Não | Não |
| Usuários | Sim | Sim | Não | Não | Não |
| Logs/Auditoria global | Sim | Sim | Não | Não | Não |
| System Health | Sim | Sim | Não | Não | Não |
| Schema Repair | Sim | Sim | Não | Não | Não |
| Homologação | Sim | Sim | Não | Não | Não |

## Policies oficiais

- `FullAdminOnly`: ADMIN, ADMINISTRADOR
- `GedAccess`: ADMIN, ADMINISTRADOR, ADMINISTRADOROPHIR, ARQUIVISTAOPHIR
- `HospitalDocumentsAccess`: ADMIN, ADMINISTRADOR, ADMINISTRADOROPHIR, ARQUIVISTAOPHIR, HOSPITAL
- `LoansView`: ADMIN, ADMINISTRADOR, ADMINISTRADOROPHIR, ARQUIVISTAOPHIR
- `LoansManage`: ADMIN, ADMINISTRADOR, ADMINISTRADOROPHIR
- `LoansRequest`: ADMIN, ADMINISTRADOR, ARQUIVISTAOPHIR
- `ProtocolRequest`: ADMIN, ADMINISTRADOR, ARQUIVISTAOPHIR
- `ProtocolManage`: ADMIN, ADMINISTRADOR, ADMINISTRADOROPHIR
- `SystemAdmin`, `SystemHealth`, `ParametersAdmin`, `UsersAdmin`, `LogsAccess`, `SchemaRepair`: ADMIN, ADMINISTRADOR

## Auditoria de acesso negado

Tentativas bloqueadas por autorização são registradas como `ACCESS_DENIED` com usuário, roles exigidas pela policy, método HTTP, path, query string, status code, IP, user agent, correlation id e trace id. A UI exibe página 403 amigável com o texto “Você não possui permissão para acessar esta funcionalidade.” e botão “Voltar para minha área”.

## Checklist de testes funcionais

### ADMIN

1. Login ADMIN.
2. Validar todos os menus.
3. Acessar `/Ged`, `/HospitalDocuments`, `/Loans`, `/Protocolo`, `/Parameters`, `/Users`, `/SystemLogs`, `/SystemHealth/Schema`.
4. Validar schema repair conforme ambiente.

### ADMINISTRADOR

1. Login ADMINISTRADOR.
2. Validar equivalência total ao ADMIN.
3. Confirmar menus e rotas administrativas.

### ADMINISTRADOROPHIR

1. Login ADMINISTRADOROPHIR.
2. Confirmar ausência de Parameters/Users/SystemLogs/SchemaRepair.
3. Acessar HospitalDocuments, Central Operacional, fila de protocolo e Loans do setor.
4. Aprovar/entregar/devolver apenas empréstimos do setor.
5. Acesso indevido deve retornar 403 e gerar auditoria.

### ARQUIVISTAOPHIR

1. Login ARQUIVISTAOPHIR.
2. Acessar HospitalDocuments, Solicitar Protocolo, Minhas Solicitações e Solicitar Empréstimo.
3. Confirmar ausência de aprovação/manuseio e administração global.
4. Acesso indevido deve retornar 403 e gerar auditoria.

### HOSPITAL

1. Login HOSPITAL.
2. Confirmar apenas busca hospitalar.
3. Bloquear GED administrativo, Loans e Protocolo administrativo.

## Testes diretos por URL

Validar acesso direto a `/Users`, `/Parameters`, `/SystemLogs`, `/SystemHealth/Schema`, `/Loans/{id}`, `/Loans/{id}/Approve`, `/Protocolo?visao=entrada` e ações de manuseio. ADMIN/ADMINISTRADOR acessam tudo; perfis restritos acessam apenas seu escopo; tentativas indevidas retornam 403 e geram auditoria.
