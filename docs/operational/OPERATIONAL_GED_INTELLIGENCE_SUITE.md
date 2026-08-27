# Operational GED Intelligence Suite — Funcionalidades, Regras e Design

## Objetivo da entrega

Consolidar a operação documental já disponível no InovaGED e fechar a lacuna de relatórios operacionais em ambientes com versões diferentes do schema, preservando isolamento por tenant, rastreabilidade e navegação Atlas UI.

## Módulos alterados

- **Relatórios operacionais:** catálogo executivo, métricas reais do tenant, distribuição por pasta e exportação CSV.
- **Database Readiness:** integração contextual quando a estrutura documental ainda não está instalada.
- **Auditoria:** exportações usam o evento canônico `REPORT_EXPORTED`.
- **Atlas UI:** alerta acionável de prontidão, métricas, filtros, tabela e empty state.

Os módulos GED, Documento 360, Etiquetas, Acervo Físico, Classificação, Smart GED, Assistente, Workflow e Administração permanecem integrados pelo catálogo e suas rotas existentes.

## Regras de negócio implementadas

1. Toda consulta de relatórios mantém `tenant_id` derivado do contexto autenticado; o usuário não informa IDs técnicos.
2. A central verifica tabelas e colunas antes de compor consultas opcionais de OCR, classificação e pastas.
3. Ausência da tabela documental produz estado de prontidão e link para `/DatabaseReadiness`, em vez de erro PostgreSQL `42P01`/`42703`.
4. Ausência de tabelas opcionais degrada métricas com segurança, sem fabricar dados.
5. Exportação rejeita intervalo invertido, limita o resultado a 50.000 linhas, gera CSV com escaping e registra auditoria.
6. A exportação também inspeciona `title`, `status`, `created_at`, `folder_id` e `folder.name`; colunas opcionais ausentes recebem fallback explícito e filtros incompatíveis retornam orientação amigável.
7. O registro auxiliar em `ged.report_export_audit` somente é executado quando a tabela existe.

## Rotas novas ou consolidadas

- `GET /Reports`
- `GET /Reports/Documents`
- `GET /Reports/Labels`
- `GET /Reports/PhysicalArchive`
- `GET /Reports/Workflow`
- `GET /Reports/Retention`
- `GET /Reports/ExportDocumentsCsv`

As rotas especializadas encaminham o usuário às visões operacionais maduras, sem exigir identificadores manuais.

## Migrations

Nenhuma migration foi criada. A evolução é compatível com schema legado por introspecção e não altera dados.

## Integrações e design

A página usa cabeçalho, métricas, painéis, data state, ícones e estilos Atlas existentes. O alerta de indisponibilidade conduz diretamente ao diagnóstico de banco. O catálogo integra GED, OCR, Classificação, Temporalidade, Etiquetas, Acervo Físico, Workflow, Auditoria e Administração.

## Validações e auditoria

- Tenant obtido de `ICurrentContext` em todas as consultas.
- Datas validadas antes da consulta de exportação.
- CSV protegido por aspas e duplicação de aspas internas.
- Evento crítico: `REPORT_EXPORTED`, com formato, relatório, quantidade e filtros.
- Erros normais de validação não geram incidente técnico.

## Como testar

1. Autenticar com permissão `Relatorios` e abrir `/Reports`.
2. Confirmar métricas e distribuição restritas ao tenant autenticado.
3. Remover, em uma base descartável, uma estrutura opcional de OCR/classificação ou uma coluna legada usada na exportação e confirmar o fallback sem HTTP 500.
4. Abrir cada rota especializada e confirmar o redirecionamento.
5. Exportar CSV com e sem filtros; conferir encoding, cabeçalho e auditoria `REPORT_EXPORTED`.
6. Informar período invertido e confirmar HTTP 400 amigável.
7. Executar os builds e gates descritos no relatório de execução.

## Pendências futuras

- Oferecer exportação CSV dedicada para cada visão operacional.
- Acrescentar testes de integração PostgreSQL para todas as combinações históricas de schema.
- Ampliar filtros salvos e agendamento de relatórios conforme governança de cada tenant.
