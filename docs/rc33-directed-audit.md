# RC33 — auditoria dirigida

## JÁ FUNCIONA
- `/Ged/Uploads` é o histórico único, com lote/item, retry, acknowledge, consistência, CSV e documentos criados.
- Upload simples, em lote e chunk compartilham os serviços existentes; RC32 já encaminha a classificação.
- O Label Studio já possui starter service, renderer oficial, diff service, validação, versões, thumbnail e wizard.
- Live preview de JSON ainda não salvo já é read-only e não cria impressão.
- Layers, agrupamento, atalhos, pan/zoom e validação visual já existem no designer.

## PARCIAL
- Histórico tinha dados reais, mas linguagem técnica, enum cru e ausência de visão operacional.
- Detalhe do lote já selecionava somente `DocumentId` válido e suportava ações existentes, porém não modelava a conferência.
- Diff existia no backend; a navegação visual ainda é limitada.
- Starter existe, mas falta o endpoint dedicado de mini-preview do wizard.

## FALTA
- Busca de sujeitos reais com ACL por tipo e preview real dedicado.
- Command oficial de bulk de classificação com resultado por item e recálculo de retenção.
- Transferência server-side de seleção pós-upload para BatchPrint.
- Sugestão pós-OCR integrada à conferência e atualização SignalR/polling.
- CRUD completo dos blocos reutilizáveis e import/export seguro.

## REDUNDÂNCIA
- Criar outro histórico, upload, renderer ou printer duplicaria contratos existentes; RC33 deve evoluir os pontos atuais.

## BUG
- Save do draft não tinha compare-and-swap: duas sessões podiam sobrescrever silenciosamente o mesmo JSON.
- A UI operacional exibia enum e identificadores técnicos.

## RISCO
- Consultas de sujeitos precisam combinar tenant e ACL; tenant isolado não é autorização suficiente.
- Preview não pode registrar trace/job/history.
- Migrations devem permanecer aditivas e compatíveis com ambientes anteriores.

## AÇÃO RC33
- Evoluir o histórico existente para Entradas documentais com KPIs/filtros derivados dos lotes.
- Adicionar persistência mínima de conferência sem alterar `document.status`.
- Adicionar `lock_version`, CAS no repositório, HTTP 409 e preservação local com autosave interrompido.
- Manter os demais gaps explícitos até implementação end-to-end e testes com PostgreSQL.
