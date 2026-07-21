# Homologação CMS operacional

A homologação deve executar `server-linux`, `agent-windows` e `security-guards`, aplicar migrations duas vezes em PostgreSQL limpo e em cenários legados, gerar PKI sintética e validar com OpenSSL `cms -verify -binary -inform DER -in assinatura.p7s -content documento.bin -noverify -out /dev/null`.
