# Causa raiz do CS1503

## Evidência inicial

SHA de partida: `de635a579eb3d882c320bd59143a71be7f1b5c2f`.

As duas chamadas de auditoria do login passavam `user.UserId.ToString()` na quinta posição de `IAuditWriter.WriteAsync`. Essa posição é `Guid? entityId`; por isso o compilador reportava CS1503. O hotfix remove a conversão e encaminha o `Guid` pelo serviço tipado de auditoria.

O build não pôde ser executado neste ambiente porque o executável `dotnet` não está instalado. A validação feita localmente limitou-se a verificações estáticas e de formato.
