# Labels Branding Fix RC5 - History SQL, Logo Preview/Print e UX Premium

## History
O erro PostgreSQL 42883 era causado pela soma de um parâmetro de tipo não determinado com `interval`, permitindo inferência incorreta na comparação com `timestamptz`. O filtro agora calcula no C# o limite final exclusivo e compara timestamp com timestamp. A consulta usa apenas o snapshot JSON para metadados opcionais, mantendo compatibilidade com schemas anteriores; ausências são apresentadas como não registradas.

## Fluxo da logo enviada
O upload aceita PNG, JPEG e WebP validados por extensão, MIME e assinatura, gera nome físico aleatório, hash SHA-256 e caminho relativo por tenant. A rota autenticada de arquivo valida tenant, estado, confinamento no webroot e existência. O resolvedor compartilhado aplica a prioridade: seleção do assistente, vínculo do modelo, padrão do tenant e sem logo. Preview e impressão recebem a mesma decisão imutável e as etiquetas renderizam a imagem real pela partial `Branding/_PrintLogo`.

A seleção registra seus dados no snapshot de impressão e a migration RC5 disponibiliza colunas normalizadas para evolução da auditoria. Os modelos LocDesk Pasta, Caixa e HOL usam a mesma partial, sem texto ou desenho substituto e com `contain`/proporção como padrão.

## UX e validação
A biblioteca de logos, cadastro, edição, Branding, estúdio e PrintWizard usam hero, cards, etapas, preview real e ações explícitas. Validar manualmente upload, URL autenticada, seleção, dimensões, encaixe, posição, preview, impressão, três variantes LocDesk, QR e History com datas.

## Pendências futuras
Popular dimensões em pixels via decoder dedicado e oferecer URL pública assinada somente se surgir impressão anônima homologada.
