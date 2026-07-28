# CMS Validation Outcome

`SignatureValidationOutcomeFactory` agrega checks em cinco dimensões: `CryptographicStatus`, `CertificateStatus`, `TrustStatus`, `ValidationStatus` e `ConformityStatus`.

Regras codificadas: CMS corrompido/documento alterado invalida criptografia; certificado expirado ou ainda não válido invalida o resultado; cadeia não confiável torna o resultado indeterminado; conformidade permanece `NOT_EVALUATED` porque ICP-Brasil, revogação produtiva e carimbo RFC 3161 não pertencem a esta evolução.
