# homologacao-cms-definitiva

Documento de homologação da Evolução 04.1.6.

## Escopo

Esta entrega estabiliza o fluxo CMS destacado local sem avançar para PAdES, CAdES ICP-Brasil, revogação LCR/OCSP ou carimbo RFC 3161.

## Decisões executáveis

- O conteúdo é acessado por capability com token opaco, hash persistido, expiração e consumo atômico.
- A URL pública usa `DigitalSignature:PublicServerBaseUrl` HTTPS, sem depender do header `Host`.
- A conclusão deriva tenant e usuário do contexto autenticado e usa comando tipado para impedir conclusão por outro usuário.
- O modelo separa integridade criptográfica, estado do certificado, validação e conformidade, mantendo `NOT_EVALUATED` para conformidade nesta fase.
- O agente usa pairing por token, autenticação por headers, proteção contra replay por nonce e downloader protegido com allowlist e bloqueios SSRF.
- O pacote de evidências deve conter documento original, `assinatura.p7s`, certificado, cadeia, relatório, README e `checksums.sha256`.

## Homologação pendente

A PR deve permanecer draft até execução real e aprovação de `server-linux`, `agent-windows` e `security-guards` no GitHub Actions.
