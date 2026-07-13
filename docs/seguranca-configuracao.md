# Segurança de configuração

A inicialização executa `IStartupConfigurationValidator` e classifica achados em crítico, alerta e informativo. Críticos impedem o startup para evitar operação insegura.

## Bloqueios em Production
- String de conexão ausente.
- Senha padrão conhecida.
- Seed habilitado.
- Certificado interno autoassinado permitido.
- Schema repair em produção.
- `DetailedErrors=true`.
- `AllowedHosts=*`.
- Usuário de sistema vazio para automações.

## SystemHealth
Acesse `/SystemHealth/SecurityConfiguration` para ver item, status, severidade, valor mascarado, recomendação, origem e ambiente.

## Certificados
- TLS: certificado do site/proxy, emitido por CA confiável.
- Internos: certificados técnicos de comunicação; autoassinados apenas em Development/PoC.
- Autenticação: certificado do usuário/cliente validado no login.
- ICP-Brasil: certificado usado em assinatura digital; a regra funcional completa permanece inalterada nesta etapa.
