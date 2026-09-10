# Auditoria Direcionada RC29 — Label Studio Operational Closure

IMPLEMENTADO
- Central de Etiquetas com quatro ações principais e gavetas contextuais de ajuda.
- Canvas engine com drag, resize, rotate, snap, validation, zoom, publish e histórico de versões.
- Tabela ged.label_manual_instance criada na migração RC28 com status DRAFT/PRINTED/ARCHIVED.
- PrintWizard com stepper de 5 etapas e endpoint QuickPreview.
- BatchPrint com seleção de caixas e documentos.
- Modelos clássicos e legado LocDesk/HOL preservados e funcionais sem regressão.

INCOMPLETO
- Etiqueta avulsa sem ciclo de vida: sem tela /Labels/Manual, sem update de rascunho e sem reuso de instância na reimpressão.
- LabelCanvasFieldCatalogService não possui catálogo para ManualLabel.
- LabelCanvasFieldDto não possui metadata IsEditableInManualMode (usa hashset hardcoded no serviço).
- WizardTemplates restringe MANUAL_LABEL apenas a template.SubjectType == MANUAL_LABEL.
- Starters usam coordenadas absolutas fixas (3, 13, 24, 35, 47, 56) que extrapolam em etiquetas pequenas (50x30, 70x40).
- Galeria /Labels/Designer exibe apenas tabela simples com texto estático "Perfil visual" na coluna de cliente.
- BatchPrint estima páginas com regra arbitrária /2 no JS e não possui cálculo físico nem amostras visuais.
- QuickPreview acoplado a ContentResult sem contrato tipado e sem códigos de erro canônicos.
- Designer Edit.cshtml é monolítico e não foi componentizado em partials reutilizáveis.
- Histórico de impressões classifica etiqueta avulsa como "Lote" sem link para a instância manual.

BUG
- snapshot_sha256 na impressão Canvas é calculado antes do traceCode e qrPayload definitivos serem gerados.
- labels-batch.js usa alert() síncrono para validação de etapas.
- Starter 50x30 posiciona campos fora do limite do Canvas gerando falha de renderização.
- Falha na impressão de ManualLabel pode deixar estado inconsistente se atualizado antes do sucesso.

RISCO
- Duplo clique ou retentativa de impressão gera múltiplos registros sem controle de concorrência por ClientActionId.
- Duplicação de template para outro cliente pode contaminar dados com fallbacks e logos da marca anterior.
- Quebra de compilação Razor caso sejam usadas expressões C# complexas ou ??[] em views.

AÇÃO RC29
- Criar catálogo ManualLabel com campos genéricos e automáticos neutros e metadata IsEditableInManualMode.
- Implementar starter genérico ETIQUETA_AVULSA e calculadora geométrica LabelCanvasStarterLayoutCalculator.
- Criar Central de Etiquetas Avulsas /Labels/Manual com ciclo completo (New, Edit, Preview, Print, Reprint, Duplicate, Archive).
- Unificar regra de compatibilidade de templates para Box, Document, Batch e ManualLabel.
- Criar ILabelPreviewService tipado e endpoint POST /Labels/Batch/Plan com amostras reais e cálculo físico.
- Implementar thumbnail leve SVG/HTML com lazy loading na galeria /Labels/Designer e resolver clientes sem N+1.
- Componentizar Edit.cshtml em partials modulares (_DesignerToolbar, _DesignerLibrary, _DesignerCanvas, etc.).
- Garantir fidelidade de trace com snapshot auditado contendo QR real e implementar idempotência por ClientActionId.
- Escrever testes automatizados e validador no Doctor labels-studio-operational-rc29.
