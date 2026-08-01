# Relatório da evolução 04.1.32 — Nexus Workspace

## Identificação

- SHA inicial: `d5e296ef393b1b78b93ce2486dbb8748ef9dc176`
- Branch: `codex/evolucao-04-1-32-nexus-workspace`
- Estratégia de rollback: reverter os commits desta branch, sem rollback de schema, pois a entrega não altera a migração aditiva existente.

## Entregue nesta iteração

- Diagnóstico verificável da base, contratos de JavaScript e lacunas de persistência.
- Fundação Nexus com paleta azul/verde, três níveis de superfície, tipografia local, dimensões oficiais, ícones e movimento entre 120 e 240 ms.
- Onze estados ilustrados em SVG local, acessível, sem script ou dependência externa.
- GED em superfície contínua com dimensões configuráveis para árvore e preview.
- Redimensionamento da árvore (240–420 px) e preview (340–600 px) por ponteiro ou teclado, com cache imediato e evento de integração.

## Preservado


Foram preservados Login, App Shell, Busca Global autorizada, GED Explorer, `GedBulkUpload`, chunking, retry, seleção documental, drag and drop, Upload Center, IDs, classes e atributos consumidos pelos módulos existentes. Nenhum projeto de teste, workflow, gate, snapshot, golden ou script de teste foi alterado.

## Funcionalidades não concluídas

Esta iteração não declara como prontas integrações server-side de preferências, favoritos, recentes, visões salvas, fila por perfil, atividades, notificações ou Assistente documental. Exibir essas funções sem repositórios, autorização e dados reais violaria a regra contra recursos cenográficos. O cache das dimensões do GED é imediato no navegador; a sincronização com `ged.user_workspace_preference` permanece pendente.

Também permanecem pendentes a extração completa dos partials do GED, metadados editáveis, histórico, versões, relacionados, colunas configuráveis, Dashboard por perfil e Busca Inteligente 3.0.

## Riscos restantes

- A View do GED continua extensa e contém estilos e scripts legados inline.
- Folhas históricas do GED possuem regras concorrentes de grid; a camada `pages/ged-workspace.css` usa especificidade e ordem para manter o contrato atual.
- A validação visual autenticada exige banco e runtime configurados.
- A ausência do SDK .NET no ambiente de execução impede confirmar build, publish e execução local nesta máquina.

## Checklist

- [x] Branch isolada da base informada
- [x] Diagnóstico real publicado
- [x] Fundação visual Nexus
- [x] SVGs locais sem dependência externa
- [x] GED com três regiões e divisores
- [x] Painéis redimensionáveis com limites e teclado
- [x] Cache imediato das dimensões
- [x] JavaScript validado por `node --check`
- [ ] Persistência server-side das dimensões
- [ ] Produtividade pessoal completa
- [ ] Dashboard por perfil e Minha Fila
- [ ] Preview documental completo
- [ ] Atividade e notificações persistidas
- [ ] Assistente contextual com fontes
- [ ] Build Debug, Release e publish (SDK indisponível)
