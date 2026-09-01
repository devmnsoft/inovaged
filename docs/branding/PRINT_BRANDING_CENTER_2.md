# Print Branding Center 2.0

## Objetivo
Centralizar a identidade visual de impressão por tenant, cliente, contrato e template, usando exclusivamente logos oficiais cadastradas.

## Rotas
As rotas autenticadas ficam sob `/Administration/PrintBranding`: central e perfis, criação, detalhes, edição, padrão, arquivamento, preview, vínculos e teste de impressão.

## Banco de dados
A migration `2026_09_01_print_branding_center_2.sql` cria `print_branding_profile`, `print_branding_binding` e `print_branding_audit_event`. Ela depende de `ged.brand_asset`, criada pela migration de upload de logos.

## Perfis visuais
Um perfil guarda cliente, contrato, órgão, duas logos opcionais, conteúdo do cabeçalho/rodapé, largura em milímetros, papel, orientação e margens. Os seletores exibem apenas assets ativos do tenant.

## Vínculos por template
Vínculos associam contexto e chave catalogada a um perfil. Incluem LocDesk Pasta, Caixa e HOL, modelos de fábrica, medição, Portal do Fiscal, governança, despacho e capa.

## Aplicação em etiquetas e documentos
O resolver compartilhado atende etiquetas, relatórios e documentos. Os partials `_PrintableBrandHeader` e `_PrintableBrandFooter` permitem adoção progressiva sem alterar documentos legados.

## Preview e teste de impressão
O preview usa a rota autenticada do asset, `object-fit: contain`, dimensões em milímetros e nenhum filtro. O teste A4 remove navegação, fundos e sombras no modo de impressão.

## Regras de prioridade
1. Perfil escolhido; 2. logo escolhida; 3. vínculo específico; 4. perfil padrão; 5. fallback seguro sem logo. O fallback informa que não há branding e não causa erro 500.

## Segurança
Controller autorizado, antiforgery em POST, consultas parametrizadas, isolamento por `tenant_id`, catálogo fechado de contextos/chaves e validação de que cada logo pertence ao tenant. Caminhos físicos não são apresentados.

## Auditoria
Criação e definição de padrão geram eventos estruturados. A tabela aceita também atualização, arquivamento, vínculo, preview, teste e eventos futuros de impressão de etiqueta/documento.

## Como validar
Aplicar as migrations obrigatórias; cadastrar uma logo oficial; criar um perfil; defini-lo como padrão; vincular as três chaves LocDesk; abrir preview, teste e PrintWizard; imprimir e conferir proporção e ausência de marca simulada.

## Pendências futuras
Expandir a adoção dos partials nos relatórios legados e disponibilizar métricas de impressão por contexto no dashboard.
