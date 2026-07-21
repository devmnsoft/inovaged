# Proteção SSRF

O downloader do agente exige HTTPS, valida host contra `AllowedServerHosts`, resolve DNS, bloqueia loopback, link-local, RFC1918 e endpoints de metadados, desativa redirect automático via HttpClientHandler e baixa por streaming com limite de bytes.
