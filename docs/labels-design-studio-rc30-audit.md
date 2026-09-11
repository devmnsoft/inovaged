# Auditoria direcionada RC30

## JÁ EXISTE
- Canvas em milímetros, grade, régua, margem segura, drag, resize e rotação.
- Histórico de undo/redo por interação concluída, multisseleção, agrupamento e alinhamento.
- Autosave single-flight, recuperação local e validação de dimensões RC29.4.
- Renderizador compartilhado para preview, thumbnail e produção.
- Versionamento, revisão, publicação, eventos e isolamento por tenant.
- Galeria com thumbnail lazy, filtros e ações operacionais.
- PrintBranding resolvido por perfil, associação e fallback compatível.

## MANTER
- Contrato JSON e elementos dos templates antigos, inclusive LocDesk/HOL.
- Segurança de dimensões e parsing invariável introduzidos na RC29.4.
- Renderer, resolvedor, version snapshots e trilha de eventos existentes.
- Políticas de autorização específicas do designer.

## POLUIÇÃO UX
- Configuração técnica e identidade estavam abertas dentro do workspace.
- Toolbar misturava edição, visualização, histórico e configuração.
- Biblioteca apresentava todos os tipos, campos, blocos e camadas simultaneamente.
- Inspector mostrava propriedades irrelevantes para o tipo selecionado.
- `Edit.cshtml` concentrava shell, biblioteca, canvas, inspector e diálogos.

## FUNCIONALIDADE INCOMPLETA
- Favoritos de campos, blocos pessoais e preview com sujeito real.
- Diff semântico tipado e comparação visual de revisão.
- Import/export seguro e concorrência otimista no save.
- Drawer de atividade e gerenciamento completo de blocos.

## AÇÃO RC30
- Componentizar a view em partials funcionais e manter `Edit` como orquestrador.
- Aplicar shell fixo de três áreas, scroll próprio e densidade compacta.
- Mover configuração e dados técnicos para drawer com seção Avançado.
- Tornar abas substitutivas, categorizar campos e persistir favoritos localmente.
- Evoluir preview, diff, presets, import/export e concorrência em serviços tipados.
