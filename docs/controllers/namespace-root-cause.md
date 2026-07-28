# Causa raiz do namespace de controllers

## Problema

O arquivo `AdministrationController.cs` era o único fonte C# da aplicação Web a
declarar `InovaGed.Web.Controller` (singular). A declaração introduzia o símbolo
`Controller` sob `InovaGed.Web`, em conflito com o tipo MVC
`Microsoft.AspNetCore.Mvc.Controller` usado pelos controllers no namespace
plural.

## Correção

O namespace foi alterado para `InovaGed.Web.Controllers`. Não foram adicionados
aliases nem qualificações globais pontuais nas classes afetadas. Assim, a
resolução normal do tipo MVC é restaurada na origem, sem trocar controllers MVC
por minimal APIs ou fabricar contextos HTTP.

## Verificação estática

A busca abaixo deve terminar sem ocorrências:

```bash
rg --pcre2 -n 'namespace\s+InovaGed\.Web\.Controller(?!s\b)' InovaGed.Web --glob '*.cs'
```

Também foram pesquisadas referências a namespaces singulares `Api`, `Security`
e `Admin` e diretivas `using InovaGed.Web.Controller`; nenhuma permaneceu.

## Validação pendente

A validação compilada deve executar os builds Debug e Release do projeto Web e o
build Release da solution em um ambiente com o .NET SDK. Até isso ocorrer, a PR
deve permanecer em draft e a evolução funcional não deve começar.
