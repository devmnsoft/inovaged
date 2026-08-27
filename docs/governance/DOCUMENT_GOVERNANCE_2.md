# Document Governance 2.0

## Objetivo
Centralizar auditoria, LGPD, riscos operacionais, alertas, evidências e relatórios executivos sem romper o isolamento entre tenants.

## Rotas
`/Governance`, `/Audit`, `/Lgpd`, `/Risks`, `/Alerts`, `/Evidence`, `/Timeline`, `/Reports` e `/Export` sob o prefixo `/Governance`. Todas exigem autenticação; ações de resolução e registro usam antiforgery.

## Banco de dados
A migration idempotente `2026_08_27_document_governance_2.sql` cria alertas, evidências, logs de exportação, snapshots de risco e índices tenant-aware.

## Auditoria
A leitura usa `ged.app_audit_log` somente após introspecção do schema. Visualizações e operações relevantes são auditadas.

## LGPD
CPF/CNPJ são mascarados na camada de serviço. Payloads de evidência não são reexpostos: armazena-se envelope protegido e SHA-256 do conteúdo recebido.

## Riscos
O snapshot mais recente alimenta oito indicadores e oferece navegação à origem ou ao Database Readiness.

## Alertas
Filtros, priorização e resolução com observação obrigatória, usuário e data de resolução.

## Evidências
Código único por tenant e hash automático quando existe payload. Fontes funcionais são escolhidas em catálogo, sem exigir ID técnico.

## Relatórios e exportação CSV
Dez tipos autorizados em whitelist; não há SQL fornecido pelo usuário. O CSV é mascarado, tenant-aware e registrado em `governance_report_log`.

## Integrações
A central navega para GED, OCR, temporalidade, acervo físico, empréstimos, workflow, incidentes e prontidão. O card administrativo dá acesso direto à governança.

## Segurança
Autorização global, antiforgery, queries parametrizadas, whitelist de relatório, mascaramento e tratamento seguro de schema incompleto.

## Como validar
Aplicar migrations, autenticar em um tenant e percorrer todas as rotas. Criar um alerta no banco de teste, resolvê-lo com nota, registrar evidência e exportar CSV.

## Pendências futuras
PDF assinado e automações de workflow permanecem futuras até existir infraestrutura transacional estável para esses módulos.
