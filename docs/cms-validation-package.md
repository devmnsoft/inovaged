# cms validation package

Documento de evolução 04.1.2 para assinatura CMS destacada.

## Escopo implementado nesta PR

- Consolidação dos contratos públicos de sessão e conclusão.
- Registro DI operacional para modo AgentCms sem resolução de repositórios Noop.
- Endurecimento inicial de validação CMS, identidade de certificado, CI e diagnóstico.

## Requisitos de segurança

- Não armazenar chave privada, PIN, senha PFX ou tokens em texto puro.
- Manter Cache-Control no-store nas APIs de assinatura.
- Restringir resultados criptográficos: conformidade permanece NOT_EVALUATED e cadeia/revogação sem LCR/OCSP permanecem INDETERMINATE.
- Aplicar tenant, usuário, política e idempotência antes de qualquer uso produtivo amplo.

## Pendências controladas

- Execução real de restore/build/test depende de SDK .NET no ambiente ou CI.
- Fluxos Windows dependem do job agent-windows.
- Homologação OpenSSL deve ser coletada como artefato no CI.
