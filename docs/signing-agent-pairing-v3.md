# Pairing v3 do Signing Agent

Rotas sensíveis exigem headers `X-InovaGed-Pairing-Token`, `X-InovaGed-Origin`, `X-InovaGed-Request-Nonce` e `X-InovaGed-Agent-Protocol`. Nonces repetidos são rejeitados pelo serviço de replay. O código de pairing gera token local e permite revogação.
