# Reconstrução da solution

`InovaGed.sln` possui formato Visual Studio 12, um único bloco `Global`, encerramento final `EndGlobal` e onze projetos: Domain, Application, Infrastructure, MVC, WebApi, Operations Worker, Portability Verifier, Signing Agent e três projetos de testes. O arquivo temporário `.sln.invalid` não é versionado.

A validação autoritativa é `dotnet sln InovaGed.sln list`, seguida de restore e build Release. O teste `SolutionStructureTests` protege unicidade e ausência de conteúdo após `EndGlobal`.
