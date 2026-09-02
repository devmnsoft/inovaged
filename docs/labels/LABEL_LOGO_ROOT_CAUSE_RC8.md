# Labels Upload Logo Root Cause Fix RC8

## Escopo e método

A investigação seguiu o valor desde o `brand_asset.storage_relative_path`, pelo resolvedor, até o HTML Razor compartilhado por preview e impressão. A evidência reproduzível no repositório é coberta por `BrandAssetImageServiceTests`, `LabelsLogoRenderingContractTests` e pelo comando `labels-logo-rendering`. O ambiente desta revisão não contém sessão de navegador nem credenciais/banco operacional; por isso nenhum resultado de produção foi inventado.

## Causa real encontrada

Havia duas violações no último trecho do fluxo:

1. O leitor combinava `ContentRootPath/wwwroot` diretamente com o valor legado. Registros antigos contendo `wwwroot\\uploads\\...` ou `~/wwwroot/uploads/...` viravam, respectivamente, `wwwroot/wwwroot/uploads/...` ou um caminho inexistente. `File.Exists` falhava, embora o upload físico existisse sob o primeiro `wwwroot`.
2. `_PrintLogo.cshtml` ainda aceitava `LogoUrl` como fallback. Isso contrariava o contrato Data URI e reintroduzia autenticação/cookie/URL no preview de impressão. O botão dependia apenas do listener externo, sem o fallback inline solicitado.

Portanto, o defeito não estava na arte LocDesk: era a resolução incompatível do caminho persistido seguida de um fallback web no renderizador.

## Evidências ponta a ponta

| Evidência | Antes | Depois / prova |
|---|---|---|
| HTML da logo no preview | podia resolver `LogoUrl` | `src="@Model.PrintImageSource"`; em execução o valor é `data:image/<tipo>;base64,…` |
| `SelectedLogoAssetId` no POST `/Labels/Preview` | campo existente, sem telemetria conclusiva | está dentro do form principal e loga somente “preenchido=true/false” |
| `SelectedLogoAssetId` no POST `/Labels/Print` | mesmo binding, sem telemetria conclusiva | os dois POSTs chamam `ProcessWizard`; log identifica `Preview`/`Print` sem registrar GUID/base64 |
| `/Administration/BrandAssets/{id}/File` | 404 quando o path legado montava `wwwroot/wwwroot` | usa o mesmo serviço normalizado e retorna arquivo somente para asset ACTIVE do tenant; caso ausente permanece 404 seguro |
| Arquivo físico | falso negativo para caminhos legados | `File.Exists` é executado apenas depois de normalização e contenção sob webroot |
| `storage_relative_path` | formatos antigos com `\\`, `~/` e `wwwroot/` falhavam | normalizado para `uploads/branding/{tenant}/{guid}.ext`; absoluto e traversal são rejeitados |
| Tenant | consulta exige `id`, `tenant_id`, `ACTIVE` e `reg_status='A'` | preservado; asset de outro tenant não é lido |
| `ResolvedPrintLogo.HasLogo` | true para asset resolvido | continua true mesmo quando bytes faltam, permitindo aviso correto |
| `ResolvedPrintLogo.ImageLoaded` | false no falso negativo do path | true somente quando bytes não vazios são lidos |
| `PrintImageSource` | podia cair em rota | somente Data URI chega ao `<img>`; partial não usa `LogoUrl` |
| Logo ausente | risco de fallback quebrado | nenhum `<img>` é emitido; `LoadError` é mostrado fora da etiqueta |
| Imprimir agora | apenas listener JS | `onclick="window.print(); return false;"` e listener externo redundante |

O log seguro produzido pelos POSTs contém `TemplateCode`, presença de `SelectedLogoAssetId`, `HasLogo`, `ImageLoaded` e se a origem começa com `data:image/`; nunca contém o base64.

## Teste controlado RC8

A prova automatizada usa paths de fixture e contrato de renderização, sem persistir base64 no relatório.

- ID da logo testada: determinado em runtime pelo asset selecionado (não disponível sem banco operacional).
- ContentType coberto: `image/png`, `image/jpeg` e `image/webp` no serviço.
- Tamanho: bytes não vazios obrigatórios; upload limitado a 5 MiB por padrão.
- `PrintImageSource` começa com `data:image/`: **sim**, por construção do serviço e guarda estática do Doctor.
- Preview exibe logo: **sim no contrato de renderização**; validação visual operacional requer sessão autenticada.
- Página de impressão exibe logo: **sim no mesmo contrato/partial**; validação visual operacional requer sessão autenticada.
- “Imprimir agora” chama `window.print()`: **sim**, inline e por JS externo; abertura do diálogo requer navegador interativo.

## Correção aplicada

- Normalização retrocompatível de barras e prefixos `~/`/`wwwroot/`, com rejeição de caminhos absolutos e traversal.
- Uso exclusivo de `PrintImageSource` na partial e supressão total do `<img>` quando a imagem não carregou.
- Log seguro e equivalente nos POSTs Preview e Print.
- Fallback inline e listener externo para `window.print()` em todas as páginas reais de impressão.
- Check estático `labels-logo-rendering` e testes da normalização segura.

## Checklist QA RC8

- [x] Binding de `SelectedLogoAssetId` dentro do form principal.
- [x] Preview e Print passam pelo mesmo `ProcessWizard` e resolvedor.
- [x] Data URI é a única fonte aceita pela etiqueta.
- [x] Asset ausente não cria imagem quebrada.
- [x] LocDesk Pasta, Caixa e HOL usam o render model/partial compartilhado.
- [x] Botão de impressão possui dois caminhos para `window.print()`.
- [x] Isolamento por tenant, ACTIVE, antiforgery e autorização preservados.
- [x] Caminho físico não é exposto.
- [ ] Smoke visual com banco operacional e navegador autenticado (indisponíveis neste ambiente; não afirmar resultado fictício).
