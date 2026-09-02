# Logo Layout Studio for Print

## Objetivo
O estúdio permite selecionar uma imagem real cadastrada, visualizar e salvar tamanho, posição e encaixe por modelo, preservando proporção por padrão.

## Rotas
As rotas autenticadas ficam em `/Labels/LogoLayout`, `/{templateCode}`, `/{templateCode}/Save`, `/{templateCode}/Preview` e `/{templateCode}/PrintTest`. Documentos usam os atalhos `/Documents/PrintBranding`, `/Save` e `/TestPrint`.

## Como selecionar e enviar logo
Selecione um card visual de asset ativo do tenant. “Enviar nova logo” abre o cadastro seguro; IDs não são digitados e assets arquivados ou de outro tenant são recusados.

## Como redimensionar, posicionar e preservar proporção
Informe largura de 10–90 mm, altura de 5–60 mm e offsets de -30–30 mm. Use as nove âncoras, `CUSTOM` ou os botões de movimento. “Manter proporção” vem ativo e `CONTAIN` é o encaixe recomendado; `FILL` gera alerta.

## Etiquetas e documentos
Salve por template para LocDesk Pasta, Caixa, HOL e modelos Factory. `DOCUMENT_PRINT_HEADER`, relatórios e termos disponibilizam o mesmo ajuste para documentos, sem alterar os documentos legados quando não há configuração.

## Preview em tempo real e teste de impressão
O JavaScript atualiza imagem, dimensões, posição e encaixe sem recarregar ou apagar o formulário. O teste abre uma folha limpa e não registra uma impressão real.

## Validações e segurança
O servidor valida limites, antiforgery, autorização e isolamento pelo `tenant_id`. Somente assets ativos do tenant são aceitos; caminhos físicos nunca são expostos ou persistidos pelo estúdio. Alertas destacam ausência, saída da área e risco de deformação.

## Como validar
Aplique `2026_09_02_logo_layout_studio_print.sql`, abra o catálogo, ajuste cada modelo, salve, reabra e execute o teste. Confirme `CONTAIN`, proporção, margens e offsets na mídia de impressão.

## Pendências futuras
Drag-and-drop acessível, detecção de área imprimível por impressora e propagação transacional da identidade completa para todos os renderizadores especializados.
