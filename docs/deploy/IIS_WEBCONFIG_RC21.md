# `web.config` base para IIS — RC21

O arquivo versionado usa ANCM V2 em `outofprocess`: o IIS encaminha as requisições ao processo `dotnet InovaGed.Web.dll`, mantendo comportamento próximo ao Kestrel já validado. Ele não contém `<rewrite>`, portanto não depende do URL Rewrite Module. O XML e os arquivos críticos são validados por `verify-iis-publish.ps1`.

## HTTP de homologação

Mantenha `HttpsRedirection:Enabled=false` na configuração externa de Production. Isso evita `Failed to determine the https port for redirect` quando o site só possui binding HTTP.

## Ativação de HTTPS

1. Instale o certificado no servidor e crie o binding HTTPS no IIS.
2. Configure `HttpsRedirection:Enabled=true` e `HttpsRedirection:HttpsPort=443` no arquivo externo/variáveis do ambiente.
3. Recicle o AppPool e valide HTTP → HTTPS. Rewrite no IIS é opcional e deve ficar em arquivo específico do servidor, após confirmar que o módulo está instalado.

## Mudança futura para `inprocess`

Somente após estabilizar e homologar o artefato, altere `hostingModel` para `inprocess` em uma release controlada. Instale o Hosting Bundle correspondente ao .NET 8, reinicie o IIS, valide arquitetura x64, `/status`, Event Viewer e rollback. Não faça essa troca diretamente em produção.
