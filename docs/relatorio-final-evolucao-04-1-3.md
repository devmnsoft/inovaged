# relatorio final evolucao 04 1 3

Documento de homologação da Evolução 04.1.3.

## Escopo entregue

- Fluxo CMS destacado tratado como assinatura criptográfica local via agente.
- Persistência canônica em `ged.signing_session`, `ged.document_signature`, validações, evidências e eventos.
- Estados semânticos separados: criptográfico, validação/cadeia e conformidade.
- Conformidade ICP-Brasil, LCR/OCSP e carimbo do tempo permanecem fora do escopo desta evolução.

## Segurança

- Tokens devem ser armazenados apenas como hash.
- Conteúdo e `.p7s` devem ser transmitidos com `Cache-Control: no-store`.
- Pairing requer código, origem exata, nonce anti-replay e confirmação local humana.
- O agente deve restringir downloads a hosts permitidos e usar streaming com limite de tamanho.

## Homologação

- Aplicar migrations em banco limpo e reaplicar para comprovar idempotência.
- Validar CMS destacado com OpenSSL usando o documento original e a assinatura `.p7s`.
- Confirmar isolamento multi-tenant e anti-replay antes de promover a PR de draft para pronta.
