# Relatório final — hotfix 04.1.30-R1

- **SHA inicial:** `c8ee333b4818af9a8ed34a4b0eb5b6e83a34190c`
- **Branch:** `codex/hotfix-04-1-30-r1-restauracao-template-funcional`
- **Referência:** `0e0de41249d44c41a9c4d0e735ea9f3e758968e7`
- **Rollback:** reverter os commits desta branch; não há migration nem alteração de negócio.

## Entrega executada

O layout foi simplificado, os cálculos de usuário foram isolados, sidebar e topbar históricas foram restauradas, ações cenográficas foram ocultadas, feedback foi consolidado e o offcanvas foi corrigido. O login institucional atual já preservava painel, marca, azul/verde, e-mail/CPF, recuperação de senha, TenantSlug, ReturnUrl, antiforgery e validação; portanto foi mantido em vez de receber uma reconstrução de risco.

Foram adicionados contratos para shell, sidebar, topbar, mobile, feedback, controllers do menu e paridade CSS, além dos jobs `template-recovery-*` e do `template-recovery-gate` no `engineering-gate`.

## Riscos e aprovação

A PR deve permanecer draft. Screenshots autenticados, respostas HTTP por perfil e aprovação humana de Sidebar, Topbar, Login, Dashboard, GED, Mobile, Toasts, Confirmações e rotas dependem do ambiente homologado. A existência de pastas não equivale a golden aprovado. Nenhum merge automático é autorizado.
