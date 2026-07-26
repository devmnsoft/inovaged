# Composição de dependências

A composição compartilhada parte de `AddInovaGedApplication` e `AddInovaGedInfrastructure`. MVC, API e Operations Worker complementam apenas necessidades do próprio host. `DependencyInjectionCompositionTests` valida o provider com `ValidateScopes` e `ValidateOnBuild` e protege contratos críticos contra duplicidade não intencional.
