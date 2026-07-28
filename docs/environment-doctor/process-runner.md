# Process runner seguro

`SafeProcessRunner` usa `ArgumentList`, não inicia shell, captura stdout/stderr assincronamente, limita saída, mede duração, respeita cancelamento e timeout e encerra a árvore no timeout. Falhas usam códigos estáveis e a saída passa pelo sanitizador.
