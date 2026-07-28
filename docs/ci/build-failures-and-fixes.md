# Falhas de restore, build e testes

O SDK .NET não está instalado neste executor; todos os comandos `dotnet` do gate inicial falharam com `command not found` (exit 127). Isso é limitação ambiental, não falha atribuída ao código. Nenhum warning foi suprimido e nenhum pacote criptográfico foi adicionado sem evidência de compilação. O workflow preserva restore, build e testes reais e publica seus diagnósticos.
