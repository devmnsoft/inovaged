# Signing Agent Pairing v7

O pairing final deve usar challenge com ID, código aleatório exibido localmente, hash do código, origem, expiração, tentativas, aprovação local, uso único e pairing resultante. Endpoints alvo: `POST /pairing/challenges`, `GET /pairing/challenges/{id}/confirm-ui`, `POST /pairing/challenges/{id}/confirm-local`, `POST /pairing/complete` e `DELETE /pairings/{id}`.
