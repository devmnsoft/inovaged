# Server Labels RC18 — achados dos logs IIS

## Achados

- `POST /` com 405 indicava submit sem destino efetivo. Os formulários de etiquetas agora permanecem POST tradicionais com antiforgery e cada comando declara sua `formaction` de Labels.
- `GET /data:,` com 400 indicava uma origem de imagem vazia ou malformada tratada como URL. A partial de impressão só emite `<img>` depois da validação central de uma Data URI de imagem Base64 não vazia.
- Foram observadas sondagens para `.env`, `.git`, GraphQL, PHP CGI, Actuator, status de servidor e arquivos de gerenciador.

## Correções e hardening

O serviço de imagem rejeita tipo não-imagem, arquivo sem bytes e Base64 vazio. O middleware, executado antes de arquivos estáticos, responde 404 sem detalhes para os caminhos sensíveis conhecidos. O `web.config` aplica uma segunda barreira no IIS, e `robots.txt` desencoraja indexação das áreas internas.

Não existe `manager.html` nem `assets/js/app.js` no código-fonte ou no `wwwroot`; portanto, o 200 observado não pertence à publicação versionada do InovaGED. `manager.html` também foi bloqueado explicitamente para impedir que um artefato residual seja servido em uma publicação futura. A pasta de publish deve ser limpa antes do deploy, em vez de receber cópia incremental sobre conteúdo antigo.

## Como validar

1. Execute `dotnet run --project InovaGed.Environment.Doctor -- server-labels-iis-quality`.
2. Faça publish em uma pasta vazia e confirme a ausência de `manager.html`.
3. Verifique que `/.env`, `/.git/config`, `/graphql`, `/php-cgi/php-cgi.exe` e `/manager.html` retornam 404 sem stack trace.
4. Abra o assistente e o LocDesk, submeta cada ação e confirme nos logs que não há `POST /`.
5. Inspecione a marcação impressa e confirme que uma logo existente começa por `data:image/` e que nenhuma imagem é emitida quando o asset falha.

## Pendências operacionais

A confirmação de ausência de novos eventos nos logs IIS e a abertura do diálogo nativo de impressão dependem de homologação no navegador e servidor Windows. Limpar o diretório físico do site antes do novo deploy continua obrigatório para remover resíduos não versionados.
