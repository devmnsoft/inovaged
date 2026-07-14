# Diagnóstico de evolução pós-build

| Módulo | Classificação | Evidência/observação | Próximo passo |
|---|---|---|---|
| GED | funcional com pendências | Há controllers, serviços de documentos, upload, classificação, movimentação e auditoria. | Fechar lacunas de manifesto, timeline probatória e consistência de versões. |
| Upload | funcional com pendências | Existem fluxos simples, lote e chunk. | Completar retomada, rollback operacional e limpeza automatizada auditável. |
| OCR | funcional com pendências | Existem serviços e documentação de ambiente OCR. | Expandir retry/backoff, qualidade e vínculo rígido com versão. |
| Preview | parcial | Há referências e pendências no Guardião. | Consolidar geração Office/imagem/PDF, timeout e fallback. |
| Classificação | funcional com pendências | Há controllers, pendências e contadores. | Integrar dossiês, SLA e regras automáticas. |
| Temporalidade | parcial | Há instrumentos, destinação e relatórios. | Automatizar recálculo, holds, termos e aprovação. |
| Assinatura | parcial | Há módulo/controller de assinatura. | Consolidar cadeia, revogação, relatório e lote. |
| Empréstimos | funcional com pendências | Há módulo Loans e worker de atraso. | Completar cobrança, perda/dano e QR Code. |
| Acervo físico | parcial | Há physical/loans/localização. | Integrar localização obrigatória, divergências e inventário. |
| Workflow | parcial | Há controllers e serviços documentais. | Consolidar etapas, transições, paralelismo, SLA e gatilhos. |
| Auditoria | funcional com pendências | Há middleware, logs e trilhas. | Reusar na timeline probatória exportável. |
| Protocolo | funcional com pendências | Há vários controllers de protocolo. | Integrar busca, dossiês e manifesto. |
| PACS | parcial | Há tickets/quarentena em regras. | Consolidar reprocessamento e fila de integração. |
| Guardião | funcional com pendências | Já há regras determinísticas, scoring e fila abstrata. | Persistir fila, dead-letter, dashboard e decisões humanas. |
| System Health | funcional com pendências | Há health/homologação/schema repair. | Integrar Central de Pendências e notificações. |
| Workers | parcial | Existem workers específicos. | Padronizar multi-tenant, lock, retry e observabilidade. |
| Busca | parcial | Há smart/ged search. | Unificar documentos, OCR, dossiês, workflow e permissões na consulta. |
