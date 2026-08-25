# Premium UI Pass 2 — Checklist de QA visual

> Status: **pronto para homologação visual**. Validar com uma conta que possua acesso às áreas indicadas e registrar evidências nas resoluções 1920×1080, 1366×768, tablet e mobile.

| Item | Status | Como validar | Rota | Observação |
|---|---|---|---|---|
| Login | Pronto | Conferir painel dividido, foco, erros, carregamento e redução de movimento | `/Account/Login` | `/Login` só se o ambiente expuser o alias |
| Layout principal | Pronto | Verificar largura do canvas, ritmo vertical e ausência de salto | `/` | A raiz preserva o redirecionamento por perfil |
| Sidebar | Pronto | Pesquisar, recolher, expandir grupos e confirmar item ativo | Qualquer rota autenticada | Validar também offcanvas mobile |
| Topbar | Pronto | Testar busca, ações rápidas, utilitários e menu do usuário | Qualquer rota autenticada | Foco deve permanecer visível |
| Dashboard | Pronto | Conferir cards, filas, alertas e fallback vazio | `/Operations` | Conteúdo depende da permissão do perfil |
| Administração | Pronto | Conferir hero, métricas, recomendações e cards de governança | `/Administration` | Testar Users, Security e Migrations |
| Etiquetas | Pronto | Conferir hero, alerta de saneamento e cards de ação | `/Labels` | Nenhum dado fictício é criado |
| PrintWizard | Pronto | Percorrer etapas, validar ajuda, preview e ações | `/Labels/PrintWizard` | Testar erros de validação |
| LocDesk | Pronto | Conferir escala, QR, controle, volume e legibilidade | `/Labels/LocDesk` | Usar dados de homologação |
| Tabelas | Pronto | Conferir cabeçalho, hover, ações e scroll horizontal | `/Labels/History` | Repetir em prontidão e incidentes |
| Formulários | Pronto | Navegar por teclado, provocar validação e conferir botões | `/Administration/Security` | Labels devem estar associados aos campos |
| Alertas | Pronto | Validar ícone, título, orientação e ação contextual | `/DatabaseReadiness` | Não exibir stack trace |
| Empty states | Pronto | Aplicar filtros sem resultados | `/SystemIncidents` | Estado vazio não deve parecer erro |
| Responsividade | Pronto | Testar 1920, 1366, 768 e 390 px | Todas | Botões podem quebrar, nunca sobrepor |
| Ícones | Pronto | Conferir significado, alinhamento e fallback do catálogo | Todas | Não usar dependência remota |
| Modo impressão | Pronto | Abrir prévia e confirmar ausência do shell e dos controles | Views LocDesk | Validar margens físicas em mm |
| Acessibilidade | Pronto | Teclado, zoom 200%, contraste e `prefers-reduced-motion` | Todas | Confirmar ordem de leitura e regiões |

## Rotas de smoke test

`/`, `/Account/Login`, `/Administration`, `/Administration/Users`, `/Administration/Security`, `/Administration/Migrations`, `/Labels`, `/Labels/PrintWizard`, `/Labels/LocDesk`, `/Labels/History`, `/SystemIncidents`, `/DatabaseReadiness`, `/SchemaHealth`, `/ReleaseReadiness`, `/UatReadiness` e `/PostGoLive`.
