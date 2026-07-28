# Diagnóstico inicial — 04.1.19-R1

SHA inicial: `3d029c9601dd29faeebcbb44ddc2a9d90e987383`.

## Razor
A variável `section`, imediatamente depois de `@`, era interpretada como a diretiva Razor `section`, causando RZ2005/RZ1011. A view também condensava blocos, dificultando revisão.

## Schema e execução
O assert era executado quando as seis tabelas básicas não haviam sido criadas. A causa operacional é o uso de `\ir` por `database/apply_all_required_migrations.sql`: trata-se de metacomando do `psql`, não de SQL aceito por Npgsql, DBeaver ou executores genéricos.

## Actionlint
O comando passava `never` como argumento posicional após `-color`; nesta versão ele era interpretado como arquivo.

## Erros derivados e riscos de ambiente
O módulo consultava repositórios sem proteger todas as actions, podendo transformar schema parcial em HTTP 500. O container fornecido não possui `dotnet` nem `actionlint`; checkout/pull de `main` também não foi possível porque o snapshot não contém remote nem branch `main`. Assim, clean, restore, builds e lint iniciais retornaram código 127 e devem ser confirmados pelo CI.
