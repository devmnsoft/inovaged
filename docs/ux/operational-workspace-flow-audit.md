# Auditoria de fluxos — Atlas Operational Workspace 04.1.35

**Baseline:** `dab171361ccda6de6422630b3c7e4550c09700d6`  
**Método:** inventário das rotas e controles reais, tentativa de build/execução local e contagem da menor sequência disponível na interface. A homologação autenticada depende de banco, tenant e credenciais; por isso, contagens não confirmadas em navegador estão marcadas como estimativas e não como evidência de produção.

## Síntese de complexidade

| Resultado desejado | Antes | Depois | Mudança aplicada |
|---|---:|---:|---|
| Abrir documento no GED | 2 | 2 | Mantido preview lateral sem navegação adicional. |
| Enviar à pasta atual | 3 | 2 | Atalho `Ctrl+Shift+U` aciona o upload existente no contexto atual. |
| Aplicar visão/filtro salvo | 2 | 1 | Preservado acesso direto às visões existentes. |
| Mover documento | 3 | 3 | Preservado motor de drag and drop e confirmação. |
| Editar metadados | 2 | 2 | Mantido painel documental contextual. |
| Abrir Minha Fila | 2 | 1 | Comando direto na busca global operacional. |
| Repetir busca recente | 3 | 1 | Comando dedicado, sem criar outro mecanismo de busca. |
| Abrir fonte do Assistente | 1 | 1 | Mantido acesso contextual e autorização da fonte. |

## Fluxos auditados

As quantidades abaixo contam ações deliberadas, não digitação. “Página” indica troca completa; drawer, painel e popover preservam o contexto.

| Fluxo e objetivo | Cliques / páginas / modais / recargas (baseline) | Dúvidas, ações escondidas, mensagens e repetição | Melhoria aplicada |
|---|---|---|---|
| **Entrar** — autenticar e alcançar a área autorizada | 1 / 2 / 0 / 1 | O destino depende do perfil; não há ação contextual antes da autenticação. | Preservado o resolvedor de rota inicial e adicionada orientação somente após autenticar. |
| **Localizar documento** — encontrar item autorizado | 2 / 1 / 0 / 0 | Busca global era percebida principalmente como pesquisa, embora já agregasse navegação. | A busca passa a expor ações operacionais agrupadas e mantém resultados remotos autorizados. |
| **Abrir pasta** — navegar sem perder o workspace | 1 / 1 / 0 / parcial | A árvore oferece muitas ações concorrentes no menu da pasta. | Mantida navegação parcial e priorizado o clique no nome como ação principal. |
| **Enviar documentos** — adicionar arquivos à pasta atual | 3 / 1 / 1 / 0 | Upload aparecia no menu contextual e na área principal; o atalho não era global. | `Ctrl+Shift+U` despacha o evento para o `GedBulkUpload` existente. |
| **Acompanhar upload** — ver progresso e falhas | 1 / 1 / 0 / 0 | Centro e banner coexistem; mensagens de falha variam conforme origem. | Mantidos motor, progresso e rotas existentes; base de conectividade evita erro ambíguo offline. |
| **Solicitar OCR** — iniciar processamento permitido | 2 / 1 / 0 / 0 | A ação depende da seleção e pode ficar escondida no menu. | Atalhos respeitam seleção e não capturam teclas em campos editáveis. |
| **Classificar documento** — atribuir classificação | 2 / 1 / 1 painel / 0 | Estado e ação competem visualmente na listagem. | Preservado painel contextual, sem nova página ou engine paralela. |
| **Editar metadados** — corrigir dados do item | 2 / 1 / 1 painel / 0 | Feedback pode ficar distante do campo editado. | Disponibilizada camada de recuperação/feedback para integração pelas mutações reversíveis. |
| **Mover documento** — trocar pasta autorizada | 3 / 1 / 1 confirmação / 0 | Destino válido e resultado dependem do fluxo de drag and drop. | Mantido o motor existente; API `WorkspaceUndo.offer` permite recuperação quando o endpoint suportar reversão. |
| **Criar protocolo** — iniciar atendimento | 2 / 2 / 0 / 1 | A ação pode exigir localizar o módulo antes de criar. | Comando “Criar protocolo” disponível em `Ctrl+K`. |
| **Solicitar documento** — registrar solicitação | 2 / 2 / 0 / 1 | Protocolos e solicitações aparecem próximos, sem sempre esclarecer o resultado. | Mantidas rotas funcionais; command palette prioriza atendimento, sem botão cenográfico. |
| **Atender empréstimo** — agir sobre uma solicitação | 2 / 2 / 0–1 / 1 | Ações variam por estado e permissão. | Preservadas as verificações existentes; atalhos não forçam ações sem confirmação. |
| **Consultar histórico** — entender alterações | 2 / 1 / 1 painel / 0 | Histórico documental compete com outras abas. | Mantido no contexto do documento; `Esc` oferece evento único para fechar o painel ativo. |
| **Abrir notificação** — chegar ao item de origem | 2 / 1 / 1 drawer / 0 | A central não deve ser mostrada como funcional quando indisponível. | Comando apenas aciona o drawer já renderizado; nenhuma notificação fictícia foi criada. |
| **Usar Busca Inteligente** — consultar acervo autorizado | 2 / 2 / 0 / 1 | Alternância entre busca global e inteligente exige compreender seus escopos. | Command palette preserva o mecanismo existente e explicita ações versus documentos. |
| **Usar Assistente** — obter ajuda ou abrir busca assistida | 2 / 1–2 / 1 drawer / 0–1 | Provedor avançado pode não estar configurado; não se deve simular resposta. | Comando abre o Assistente existente, que mantém mensagem explícita de indisponibilidade e não escreve sem confirmação. |

## Observações e riscos

- Nenhum número operacional, favorito, recente, notificação ou atividade foi inventado.
- A camada nova é progressiva: se um módulo não estiver renderizado ou autorizado, o evento não executa uma mutação alternativa.
- `WorkspaceUndo` oferece somente a superfície e exige callback real do fluxo chamador; ações irreversíveis não recebem desfazer automaticamente.
- A verificação em navegador autenticado, console, assets e HTTP deve ocorrer em ambiente com configuração operacional válida.
