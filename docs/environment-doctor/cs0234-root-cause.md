# Causa raiz do CS0234

Dentro de `InovaGed.Environment.Doctor`, o identificador simples `Environment` era resolvido como o namespace `InovaGed.Environment`, e não como `System.Environment`. O hotfix usa o alias explícito `BclEnvironment = global::System.Environment`, preservando o nome do projeto e do assembly.
