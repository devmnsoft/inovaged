# Auditoria dirigida RC34

- IMPLEMENTADO — RC33 mantém Central de Entradas, seleção de documentos criados, `lock_version`, CAS e 409.
- SCHEMA SEM CONSUMIDOR — `document_intake_review` não possuía contrato, persistência, endpoints nem UI.
- PARCIAL — histórico traduzia lote no Index, mas o Details apresentava enums crus e regras na view.
- UX INCOMPLETA — Details usava `alert`, `confirm` e `prompt` nas ações principais.
- PARCIAL — seleção pós-upload existia, mas sem comando único de classificação ou hand-off seguro ao BatchPrint.
- IMPLEMENTADO — upload em lote, chunk, OCR/preview jobs, comandos documentais e política de acesso já existem.
- IMPLEMENTADO — `GedClassificationSuggestionService` existe; integração operacional detalhada permanece pendente.
- RISCO — “upload concluído” não comprova OCR, preview, classificação e conferência concluídos.
- PARCIAL — Designer tem renderer, starter, diff, versionamento, LivePreview e branding oficiais.
- UX INCOMPLETA — conflito era apenas `alert`; diff existia somente por endpoint; rename usava `prompt`.
- SCHEMA SEM CONSUMIDOR — não havia preset de componente nem seleção temporária de impressão.
- AÇÃO RC34 — criar serviços tenant-aware de conferência e classificação em massa usando comandos oficiais.
- AÇÃO RC34 — expor ações operacionais no Details sem diálogos nativos e traduzir estados centralmente.
- AÇÃO RC34 — criar token opaco, usuário/tenant/expiração para o BatchPrint sem GUIDs na URL.
- AÇÃO RC34 — oferecer diálogo de conflito, comparação semântica e cópia recuperada sem force-save.
- PENDENTE — preview de sujeito real com ACL fina e experiência de busca.
- PENDENTE — CRUD completo de blocos, import/export seguro e preview renderizado do starter.
- PENDENTE — SignalR/polling seletivo e sugestão pós-OCR end-to-end.
