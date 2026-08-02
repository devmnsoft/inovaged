# Evolução 04.1.34 — relatório de entrega

- **SHA inicial:** `8a58d70d9bde8d92e675de92f5cfb9b44b7e25c1`
- **Branch:** `codex/evolucao-04-1-34-atlas-experience-completion`
- **Estratégia:** consolidação incremental do Atlas existente, sem design system paralelo.

## Entregue

1. Corrigida a declaração do registry de ícones da baseline.
2. Criado ponto único `_AtlasAssets` e aplicado aos layouts autenticado e de login.
3. `AtlasIconTagHelper` aceita `app-icon` e `atlas-icon`, classes externas, tamanhos, tons, variantes, `filled`, título, label e modo decorativo.
4. Fallback permanece visível e observável: `missing` em Development e `circle-question` em Production.
5. Escala oficial completada com 14, 16, 18, 20, 24, 32 e 40 px; sprite sem `display:none` e sem interação de ponteiro.
6. Ilustrações passam por registry interno com path, dimensões, uso e padrão decorativo; URLs arbitrárias não são aceitas.
7. Resolver único de visual de arquivos cobre PDF, Word, Excel, PowerPoint, imagem, texto, CSV, ZIP, DICOM e genérico.
8. Inventário e auditoria registram explicitamente dívidas sem converter inspeção estática em falsa aprovação visual.

## Validação executada

- Parser estrutural: 89 IDs registrados presentes no sprite; 90 `symbol` únicos; nenhum duplicado, vazio ou sem `viewBox`.
- `node --check`: todos os arquivos JavaScript em `wwwroot/js` aprovados.
- Busca por `window.alert()` e `window.confirm()`: nenhuma ocorrência nas fontes JavaScript/Razor.

## Não concluído e riscos

O ambiente não possui o executável `dotnet`. Assim, restore, builds Debug/Release, publish, execução local, rotas, console, assets 404, HTTP 500 e capturas manuais não puderam ser homologados. A PR deve permanecer draft. A baseline também contém muitos símbolos semanticamente diferentes com geometria documental repetida; eles exigem redesenho e aprovação humana antes de afirmar atendimento integral.

Não foram criados nem alterados testes, snapshots, goldens, gates ou workflows, e `dotnet test` não foi executado.

## Rollback

Reverter, na ordem, os commits desta branch restaura o estado inicial. O ponto de retorno integral é o SHA inicial acima.

## Checklist de conclusão

- [x] Sprite composto uma vez em ambos os layouts.
- [x] IDs do registry presentes no sprite.
- [x] Classes, atributos externos, tons, variantes e tamanhos preservados/suportados.
- [x] Fallback visual em Development e Production.
- [x] Registry fechado de ilustrações.
- [x] Resolver central de arquivos.
- [x] Node check e guard de diálogos nativos.
- [ ] Build Debug/Release e publish (SDK ausente).
- [ ] Homologação renderizada, console, rotas e capturas.
- [ ] Redesenho individual e aprovação humana de todas as geometrias repetidas.
- [ ] Migração visual completa das páginas legadas.
