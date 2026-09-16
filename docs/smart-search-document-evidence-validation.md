# SmartSearch — roteiro de validação de perguntas aos documentos

## Limites e comportamento

- A pergunta aceita de 3 a 500 caracteres.
- São considerados no máximo 20 documentos, 12 passagens e 2 passagens por documento.
- O modo funciona sem provedor de IA e identifica a saída como **“Trechos encontrados para sua pergunta”**.
- O vínculo de página permanece vazio quando a extração não comprova a página; a interface mostra o intervalo de caracteres.
- Perguntas e respostas deste modo não são persistidas como conversa. Apenas o evento técnico, a quantidade de fontes e a cobertura parcial são auditados.
- O escopo é resolvido novamente e as fontes ativas do tenant são revalidadas imediatamente antes da resposta. Coleções também exigem o mesmo proprietário.

## Casos determinísticos

1. Uma fonte com prazo expresso: confirmar prazo, unidade, condição e referência à mesma fonte.
2. Duas fontes complementares: confirmar dois grupos de evidências, sem produzir uma conclusão sintética.
3. Pergunta sem termo presente: confirmar “Não encontrei evidência suficiente nos documentos analisados.”
4. Data condicionada: confirmar que evento e condição permanecem no trecho.
5. Negação: confirmar que “não” permanece no trecho.
6. Versões divergentes: conferir versões lado a lado pelas fontes e usar **Comparar**; não declarar prevalência.
7. OCR ausente/incompleto: confirmar limitação explícita e ausência de página inventada.
8. Protocolo `001234/2026`: confirmar preservação de zeros, barra e fonte correta.
9. OCR contendo uma instrução maliciosa: confirmar que ela é exibida apenas como dado e não altera escopo nem executa ação.
10. Revogar/remover a fonte durante o processamento: confirmar descarte com HTTP 403.
11. Mais de 20 resultados: confirmar cobertura parcial e contagens disponível/considerada.
12. Dois tenants: confirmar que identificadores do outro tenant não produzem texto nem link.

## Conferência humana semântica

Para cada trecho, abra o original, compare versão e localização, verifique se a afirmação pretendida está expressa (não apenas palavras semelhantes) e classifique divergências como comprovadas, possíveis, extração defeituosa ou objetos diferentes. Não altere documentos, prazos ou classificações a partir desta tela.
