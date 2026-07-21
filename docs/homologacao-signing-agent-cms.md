# Signing Agent CMS destacado - Evolução 04.1

Esta documentação descreve a base funcional para assinatura CMS destacada (`.p7s`) com agente local InovaGED.

## Escopo implementado

- Agente local minimal API em .NET 8, restrito a loopback (`127.0.0.1` e `::1`).
- Pairing temporário com origem exata e token de uso único.
- Listagem de certificados do usuário com chave privada, vigentes e com uso de assinatura digital.
- Operação de assinatura com confirmação explícita antes de chamar `SignedCms`.
- CMS destacado com certificado público do signatário e atributo de data de assinatura.
- Módulo de DI `AddDigitalSignatureModule(configuration)` com modo desabilitado seguro.
- Migration oficial idempotente para sessões, evidências, checks e bytes `.p7s`.

## Veracidade criptográfica

CMS destacado válido não é declarado como ICP-Brasil, CAdES, PAdES, AD-RB ou assinatura qualificada nesta etapa. A conformidade produtiva permanece `NOT_EVALUATED` ou `INDETERMINATE` até validação de política, LCR/OCSP e carimbo do tempo.

## Configuração

A seção `DigitalSignature` controla habilitação, modo `AgentCms`, TTLs, HTTPS loopback, origens permitidas e bloqueios de produção para PFX server-side e certificados internos de teste.

## Segurança

A chave privada nunca é exportada. PIN, senha PFX e documento real não são enviados a serviços externos. CPF deve ser exibido e registrado apenas mascarado; hash de busca usa SHA-256 quando extraível do certificado.

## Rollback lógico

Desabilite `DigitalSignature:Enabled`, mantenha as tabelas/evidências para auditoria e o GED volta a resolver serviços seguros `NOT_VERIFIABLE` sem remover assinaturas históricas.
