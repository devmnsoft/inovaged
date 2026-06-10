# Perfis e Permissões do InovaGED

Este documento consolida as regras oficiais de acesso por perfil. Segurança deve ser aplicada em menus, controllers, actions, endpoints AJAX, preview/download e queries com escopo.

## Perfis padronizados

| Perfil | Descrição |
|---|---|
| `ADMIN` | Administrador global com acesso total. |
| `ADMINISTRADOR` | Administrador global equivalente ao `ADMIN`, com acesso total. |
| `ADMINISTRADOROPHIR` | Operação Ophir: manuseio, aprovação e acompanhamento do setor vinculado. |
| `ARQUIVISTAOPHIR` | Solicitação e acompanhamento de protocolos/empréstimos próprios. |
| `HOSPITAL` | Usuário hospitalar de leitura, restrito à busca/visualização permitida. |

## Matriz de funcionalidades

| Funcionalidade | ADMIN | ADMINISTRADOR | ADMINISTRADOROPHIR | ARQUIVISTAOPHIR | HOSPITAL |
|---|---:|---:|---:|---:|---:|
| Dashboard | Sim | Sim | Não | Não | Não |
| Central Operacional | Sim | Sim | Sim | Não | Não |
| GED / Explorer | Sim | Sim | Setor/autorizado | Não administrativo | Não |
| HospitalDocuments / Busca Hospitalar | Sim | Sim | Sim, conforme escopo | Sim, conforme permissão | Somente leitura permitida |
| Inteligência Hospitalar | Sim | Sim | Não | Não | Não |
| Alertas e Tendências | Sim | Sim | Não | Não | Não |
| Uploads / OCR / Fila OCR / Classificação | Sim | Sim | Não administrativo | Não | Não |
| Pastas / Dossiês | Sim | Sim | Não administrativo | Não | Não |
| Guarda Física / Localizações / Caixas / Etiquetas | Sim | Sim | Não | Não | Não |
| Empréstimos - ver | Sim | Sim | Pedidos do setor | Próprios pedidos | Não |
| Empréstimos - solicitar | Sim | Sim | Não | Sim | Não |
| Empréstimos - aprovar/rejeitar/entregar/devolver | Sim | Sim | Sim, se pedido pertence ao setor | Não | Não |
| Protocolo - solicitar | Sim | Sim | Não | Sim | Não |
| Protocolo - fila/manuseio | Sim | Sim | Sim, setor vinculado | Não | Não |
| Parâmetros | Sim | Sim | Não | Não | Não |
| Usuários | Sim | Sim | Não | Não | Não |
| Logs | Sim | Sim | Não | Não | Não |
| SystemHealth / Schema do Banco / Homologação | Sim | Sim | Não | Não | Não |
| SchemaRepair | Sim | Sim | Não | Não | Não |

## Menus por perfil

### ADMIN e ADMINISTRADOR
Visualizam todos os menus globais: Dashboard, Central Operacional, GED/Explorer, Busca Hospitalar, Inteligência Hospitalar, Alertas e Tendências, Uploads, OCR, Fila OCR, Classificação, Pastas, Dossiês, Guarda Física, Localizações, Caixas, Etiquetas, Empréstimos, Protocolo, Parâmetros, Usuários, Logs, SystemHealth, Schema do Banco, Homologação, Configurações e Sair.

### ADMINISTRADOROPHIR
Visualiza somente a jornada operacional: Central Operacional, Buscar Prontuários e Documentos, Fila de Protocolos, Manuseio de Processos, Solicitações de Documentos, Empréstimos para Aprovação/Tratamento, Documentos do Setor e Sair.

### ARQUIVISTAOPHIR
Visualiza: Buscar Prontuários e Documentos, Solicitar Protocolo, Minhas Solicitações de Protocolo, Solicitar Documento/Empréstimo, Meus Pedidos e Sair.

### HOSPITAL
Visualiza apenas Buscar Prontuários e Documentos e Sair.

## Rotas e policies

| Policy | Perfis |
|---|---|
| `FullAdminOnly` | `ADMIN`, `ADMINISTRADOR` |
| `GedAccess` | `ADMIN`, `ADMINISTRADOR`, `ADMINISTRADOROPHIR`, `ARQUIVISTAOPHIR` |
| `HospitalDocumentsAccess` | `ADMIN`, `ADMINISTRADOR`, `ADMINISTRADOROPHIR`, `ARQUIVISTAOPHIR`, `HOSPITAL` |
| `LoansView` | `ADMIN`, `ADMINISTRADOR`, `ADMINISTRADOROPHIR`, `ARQUIVISTAOPHIR` |
| `LoansRequest` | `ADMIN`, `ADMINISTRADOR`, `ARQUIVISTAOPHIR` |
| `LoansManage` | `ADMIN`, `ADMINISTRADOR`, `ADMINISTRADOROPHIR` |
| `ProtocolRequest` | `ADMIN`, `ADMINISTRADOR`, `ARQUIVISTAOPHIR` |
| `ProtocolManage` | `ADMIN`, `ADMINISTRADOR`, `ADMINISTRADOROPHIR` |
| `SystemAdmin`, `SystemLogs`, `SchemaRepair`, `UsersAdmin`, `ParametersAdmin` | `ADMIN`, `ADMINISTRADOR` |
| `OperationsAccess` | `ADMIN`, `ADMINISTRADOR`, `ADMINISTRADOROPHIR`, `ARQUIVISTAOPHIR` |

## Redirecionamento pós-login

| Perfil | Destino padrão |
|---|---|
| `ADMIN` | `/Ged` ou returnUrl local permitido |
| `ADMINISTRADOR` | `/Ged` ou returnUrl local permitido |
| `ADMINISTRADOROPHIR` | `/Operations` |
| `ARQUIVISTAOPHIR` | `/Loans/New` |
| `HOSPITAL` | `/HospitalDocuments` |
| Sem perfil reconhecido | `/HospitalDocuments` |

Cada login concluído registra UserId, login, roles e redirect escolhido no log estruturado e na auditoria de aplicação.

## Escopo por setor

### Empréstimos
- `ADMIN` e `ADMINISTRADOR` veem e gerenciam todos os pedidos.
- `ADMINISTRADOROPHIR` vê e gerencia apenas pedidos cujo setor do solicitante corresponde ao setor vinculado ao usuário.
- `ARQUIVISTAOPHIR` vê os próprios pedidos e pode cancelar/responder ajuste quando o status permitir, sem ações administrativas.

### Protocolo
- `ADMIN` e `ADMINISTRADOR` veem e processam tudo.
- `ADMINISTRADOROPHIR` vê a fila do setor, manuseia processos do setor atual e aprova/devolve/finaliza conforme policy.
- `ARQUIVISTAOPHIR` cria solicitações e acompanha as próprias/participantes autorizadas, sem fila administrativa.

### HospitalDocuments
Busca, preview e download devem respeitar o mesmo critério de autorização do backend. O perfil `HOSPITAL` é sempre somente leitura.

## Auditoria de acesso negado

Quando a autorização gera 403, o sistema grava `ACCESS_DENIED` com userId, userName, roles, path, method, policy/roles exigidos, controller, action, IP, userAgent, correlationId e data em `ged.app_audit_log`, além do log técnico `ged.security_access_failure_log`.

Se `ADMIN` ou `ADMINISTRADOR` receber 403, o evento é warning operacional porque indica inconsistência de policy ou rota.
