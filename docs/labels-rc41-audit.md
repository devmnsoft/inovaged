# Auditoria funcional de Etiquetas — RC41

| Área | Estado | Evidência / risco | Ação RC41 |
|---|---|---|---|
| Canvas, versões e lock | FUNCIONA | Snapshot, hash, revisão e `lock_version` centralizados | Preservar autoridade existente |
| Validação do template | FUNCIONA | Renderer valida dimensões, margens, bindings e overflow | Reusar no preflight e publish gate |
| Dados reais | FUNCIONA | Resolver tenant-aware já abastece preview e impressão | Executar validação runtime sobre valores resolvidos |
| Print state machine | FUNCIONA | Transições e apresentação centralizadas | Não alterar |
| Wizard | UX FRÁGIL | Conferência descrevia itens, mas não agrupava achados preventivos | Adicionar painel de preflight acessível |
| Batch | PARCIAL | Planejamento e amostra existem; resultado preventivo por item não era contrato comum | Disponibilizar preflight em lote sem job/trace |
| Recomendações | PENDENTE | Seletor não possuía score explicável | Score determinístico com pesos documentados |
| Perfil de impressão | PARCIAL | Perfis e calibração existem sem ranking reutilizável | Priorizar compatibilidade exata e fallback do tenant |
| Histórico de sucesso | PARCIAL | Job registra sucesso, mas metadados podem variar por instalação | Somente pontuar quando o chamador fornecer evidência real |
| Auto-layout | PARCIAL | Alinhamento/distribuição existiam sem prévia explícita | Prévia, confirmação, respeito a lock e undo único |
| Overflow real | FUNCIONA | Renderer valida valores resolvidos | Expor como achado humano e autofix opcional |
| Condições | UX FRÁGIL | Operadores reduzidos e configuração técnica | Builder visual AND simples e whitelist |
| Estilo condicional | PENDENTE | Sem execução arbitrária permitida | Definir whitelist segura no contrato; UI completa fica incremental |
| Formatadores | PARCIAL | Formatos básicos validados | Ampliar catálogo seguro sem alterar o dado fonte |
| QR/barcode | FUNCIONA | Renderer final produz ambos e restringe fontes | Manter payload oficial server-side e formatos suportados |
| Dataset | PARCIAL | Preview real unitário existe | Contrato batch limita custo; seleção visual de cinco fica incremental |
| Designer onboarding | FUNCIONA | Tour existente | Somar checklist dispensável em `localStorage` |
| Command palette | PENDENTE | Não havia palette local | Ctrl+K, busca simples e teclado |
| Quick insert | PENDENTE | Duplo clique só editava texto | Abrir inserção no canvas vazio |
| Compatibilidade | PENDENTE | Metadados existem, sem matriz visual dedicada | Entrada no menu; endpoint/tela detalhada incremental |
| Segurança | FUNCIONA | Tenant, ACL de preview, antiforgery e fontes seguras | Nenhuma expressão/CSS/URL arbitrária |
| Performance | REGRA DUPLICADA | Preview e batch podiam repetir validação | Preflight reutiliza renderer; API batch evita render de miniaturas |

Não foi criada migration: contratos e preferências usam código, `DesignJson` e metadados existentes.
