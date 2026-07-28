# Segurança e mascaramento

`SafeMetadataSanitizer` rejeita chaves sensíveis sem diferenciar maiúsculas/minúsculas e mascara credenciais e caminhos conhecidos. Checksum comprova integridade, não autenticidade. Relatórios não devem conter connection strings, tokens, stdout bruto ou caminhos completos.
