# Labels Traceability 2.0 — QR Code, Scanner, Validação e Reimpressão Controlada

## Objetivo
Cada impressão real recebe identidade própria, código humano e token público opaco. Pré-visualizações, PrintTest e PrintSample não passam pelo registrador e não criam identidade.

## Rotas
- Internas e autenticadas: `/Labels/Scanner`, `/Labels/Trace`, `/Labels/Replacements` e `/Labels/Quality/QrCode`.
- Públicas e de exposição mínima: `/l/{token}` e `/Labels/Trace/{token}`.

## Token seguro e QR Code
O token contém 256 bits gerados por CSPRNG, é codificado em Base64 URL-safe e somente seu SHA-256 é persistido. O QR deve conter exclusivamente `/l/{token}`; `tenant_id`, `document_id`, `box_id` e dados pessoais são proibidos. O código humano segue `LBL-AAAA-NNNNNN` e não substitui o segredo público.

## Página pública e página interna
A página pública informa apenas status, tipo, TraceCode, modelo e emissão. Usuários autenticados consultam exclusivamente o próprio tenant e acessam histórico e ações internas pela central de rastreio.

## Scanner
O scanner aceita URL completa, token ou TraceCode. Leitores USB funcionam como teclado; câmera fica como evolução futura. Cada resolução válida registra origem, resultado, usuário, IP, user-agent e localização opcional.

## Reimpressão controlada e substituição
A justificativa existente continua obrigatória. Uma substituição cria identidade nova, marca a anterior como `REPLACED` e grava o vínculo em `label_replacement_event`. O formulário não aceita UUID técnico manual, apenas TraceCode/QR e seleção de modelo.

## History, Documento 360 e Acervo físico
As identidades usam `subject_type` e `subject_id` para compor as projeções de History, Documento 360 e caixas físicas sem expor esses IDs no QR. Leituras com localização podem alimentar alertas de divergência já existentes no módulo de inventário.

## Segurança
POSTs usam antiforgery; rotas internas exigem autenticação; consultas internas filtram `tenant_id`; o token puro nunca é salvo. A página anônima usa DTO mínimo e não contém paciente, CPF, prontuário, localização interna ou IDs técnicos.

## Validação visual
A tela de qualidade verifica presença, mínimo de 20 mm, quiet zone, rota curta e payload sensível. Falhas são persistidas em `ged.label_qr_quality_issue`. O QR deve ter alto contraste e não sobrepor campos.

## Como testar
1. Aplique `2026_08_31_label_traceability_2.sql` pela prontidão do banco.
2. Imprima uma etiqueta real e confira a identidade criada; uma amostra não deve criar registro.
3. Leia o QR em janela anônima e confirme a exposição mínima.
4. Resolva a mesma etiqueta no Scanner autenticado e confira o evento.
5. Crie uma substituição com justificativa e confirme os dois status/vínculo.
6. Valide modelos na tela de qualidade e execute o build da solução.

## Pendências futuras
Decodificação por câmera, avaliação ótica automática, alertas georreferenciados e projeções enriquecidas no Documento 360 podem ser adicionados sem alterar o contrato do token.
