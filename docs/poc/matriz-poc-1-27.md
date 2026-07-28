# Matriz PoC real — requisitos 1 a 27

> Estado em 2026-07-26: todos os itens permanecem `BLOQUEADO` até execução verde do gate canônico. A matriz não transforma presença estrutural em evidência de atendimento.

| Item | Classe | Requisito | Módulo | Status real | Pendência |
|---:|---|---|---|---|---|
| 1 | `PocPhase1InstrumentsTests` | Cadastro, edição e impressão de PCD, TTD e POP. | Instrumentos arquivísticos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 2 | `PocPhase1InstrumentsTests` | Inserção e movimentação de códigos preservando todo o conteúdo associado. | Instrumentos arquivísticos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 3 | `PocPhase1InstrumentsTests` | Relatórios e impressão do PCD e TTD completos ou por classe. | Instrumentos arquivísticos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 4 | `PocPhase1InstrumentsTests` | Registro de versões de códigos, TTD e POP. | Instrumentos arquivísticos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 5 | `PocPhase2RetentionTests` | Controle automático dos prazos de guarda. | Temporalidade | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 6 | `PocPhase2RetentionTests` | Criação automática da lista de temporalidade expirada. | Temporalidade | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 7 | `PocPhase4SigningTests` | Usuários com certificados ICP-Brasil para assinatura de prontuários. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 8 | `PocPhase3SecurityTests` | Correspondência obrigatória entre CPF do usuário e certificado. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 9 | `PocPhase4SigningTests` | Validação criptográfica, vigência, cadeia e revogação. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 10 | `PocPhase3SecurityTests` | Autenticação por certificado com CPF, vigência, cadeia e revogação. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 11 | `PocPhase3SecurityTests` | Níveis personalizáveis de autoridade, privilégios e sigilo. | Segurança | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 12 | `PocPhase3SecurityTests` | Integridade e detecção de mudanças nas fontes de autoridade. | Segurança | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 13 | `PocPhase6LoansTests` | Solicitação e empréstimo com protocolo automático. | Empréstimos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 14 | `PocPhase6LoansTests` | Empréstimo físico ou acesso digital, etiquetas, aprovação e cobrança automática. | Empréstimos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 15 | `PocPhase6LoansTests` | Relatórios de empréstimos por solicitante, período e tipo. | Empréstimos | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 16 | `PocPhase5PhysicalArchiveTests` | Criação de lotes. | Arquivo físico | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 17 | `PocPhase5PhysicalArchiveTests` | Cadastro e manutenção de documentos em caixas numeradas, com logs. | Arquivo físico | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 18 | `PocPhase5PhysicalArchiveTests` | Acompanhamento das etapas de tratamento documental. | Arquivo físico | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 19 | `PocPhase4SigningTests` | Assinatura eletrônica ICP-Brasil unitária ou em lote. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 20 | `PocPhase4SigningTests` | Indicação visual de documento assinado. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 21 | `PocPhase4SigningTests` | Estado válido, inválido ou não verificável. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 22 | `PocPhase4SigningTests` | Importação com detecção e validação de assinaturas. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 23 | `PocPhase4SigningTests` | Exportação validável por ferramenta externa. | Assinaturas CMS | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 24 | `PocPhase5PhysicalArchiveTests` | Localização física por imóvel, palete, estante e demais endereços. | Arquivo físico | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 25 | `PocPhase7AuditTests` | Log de todas as ações com data, hora, ação e usuário. | Auditoria | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 26 | `PocPhase2RetentionTests` | Revalidação durante relatório, com documentos numerados e paginados. | Relatórios | `BLOQUEADO` | Run canônico e evidência executável pendentes. |
| 27 | `PocPhase3SecurityTests` | Registro de falhas de controle de acesso. | Auditoria | `BLOQUEADO` | Run canônico e evidência executável pendentes. |

## Campos de evidência

O arquivo JSON adjacente é a fonte legível por máquina e registra, para cada item: classe, requisito, módulo, controller, view, endpoint, serviço, tabelas, policy, teste, passo de demonstração, resultado esperado, evidência de auditoria, status real e pendência.
