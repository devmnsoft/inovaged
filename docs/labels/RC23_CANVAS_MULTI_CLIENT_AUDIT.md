# RC23 — Auditoria curta do Canvas multi-cliente

## Já existe

- Canvas RC22 com contratos na Application, repositório tenant-aware, renderer HTML/mm, versões e eventos.
- Designer em `/Labels/Designer`, catálogo de campos, preview, publicação e impressão de teste.
- Integração do Canvas ao `PrintWizard`, `LabelPrintRegistrar`, rastreabilidade e histórico.
- `PrintBranding` com perfis, bindings por contexto/chave, dois assets de logo e perfil padrão do tenant.
- `PrintBrandingProfileId` no modelo do assistente de impressão.
- Templates clássicos e LocDesk/HOL preservados em catálogo próprio.

## Precisa alterar

- Completar `ResolvedPrintBranding` com cliente, contrato e organização, preservando consumidores.
- Acrescentar ao design somente referências/fallbacks de branding e `label_context`.
- Resolver branding na ordem: seleção do usuário, padrão do template, binding, padrão do tenant e fallback.
- Disponibilizar campos globais de identidade para todos os tipos do Canvas.
- Resolver `primaryLogo` e `secondaryLogo` exclusivamente para rotas internas seguras de assets.
- Criar cinco templates `CLIENTE_*` neutros e marcar visualmente LocDesk/HOL como legado.
- Expor identidade do cliente no editor e destacar componentes legados em seção separada.
- Tornar o seletor de branding explícito no `PrintWizard` e persistir a identidade resolvida no snapshot.
- Exibir cliente, contrato e perfil visual no histórico sem depender do branding atual.
- Atualizar migration registry, script consolidado e Doctor.

## Não tocar

- Renderização e rotas dos templates clássicos.
- Fluxos LocDesk/HOL existentes, salvo rotulagem como legado/cliente específico.
- Contrato e comportamento schema-aware do `LabelPrintRegistrar`.
- OCR, SmartGED, SmartWorkflow, classificação, temporalidade e branding administrativo existente.
- Isolamento por tenant, autorização e exigência de revisão humana para ações sugeridas.

## Riscos de regressão

- Selecionar perfil de outro tenant ou expor asset físico em vez de rota pública validada.
- Recalcular branding do histórico e perder a identidade efetivamente impressa.
- Fazer template genérico herdar textos/dados LocDesk/HOL por amostra ou fallback.
- Alterar precedência do branding para consumidores clássicos já existentes.
- Tornar colunas RC23 obrigatórias em bases antigas antes da migration.
- Publicar template global diretamente sem materializá-lo no tenant.
- Consultas opcionais do SmartAssistant falharem quando uma tabela ainda não existe.
