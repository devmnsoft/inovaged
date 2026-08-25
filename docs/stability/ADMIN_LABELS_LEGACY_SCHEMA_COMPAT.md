# Legacy Schema Compatibility Sweep - Administração e Etiquetas

## Problemas encontrados

Consultas administrativas e de etiquetas presumiam que tabelas e colunas opcionais já existiam, causando `42703` e `42P01` em instalações legadas. A leitura do catálogo de etiquetas também escolhia a tabela pela existência, mas não avaliava seu conjunto real de colunas.

## Tabelas avaliadas

Foram avaliadas as tabelas administrativas `permission`, `app_role`, `app_user`, `tenant`, `worker_execution_status`, `schema_migration_history`, `permission_evaluation_log`, `ged_processing_jobs` e `tenant_security_configuration`, além dos catálogos, impressões, histórico e rascunhos de etiquetas.

## Colunas opcionais e fallbacks

O helper central consulta `information_schema.columns` e compõe apenas expressões disponíveis. Textos podem usar `description`, `name`, `title` ou `code`; status pode usar `reg_status`, `status` ou literais seguros. O catálogo em memória continua sendo o último fallback quando nenhuma tabela de templates existe.

## Migration criada

`2026_08_25_admin_labels_legacy_schema_compat.sql` adiciona, de modo idempotente, somente colunas auxiliares ausentes nas tabelas que já existirem. Nenhum dado é removido.

## Rotas validadas

O smoke test cobre Administração/Security, Labels, PrintWizard, History e LocDesk e rejeita respostas 500 ou marcadores `42703`, `42P01` e exceção de schema não tratada.

## Pendências restantes

Ambientes sem as tabelas obrigatórias devem aplicar as migrations pela Prontidão do Banco. A validação autenticada e com dados reais permanece parte da homologação do ambiente de destino.
