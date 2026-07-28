# Desenvolvimento no Windows

Instale o SDK .NET 8 de forma consciente (`winget install --id Microsoft.DotNet.SDK.8 --source winget`), Git e os componentes da `.vsconfig`. Execute `.\eng\setup-development.ps1` e `.\eng\build.ps1`. Os scripts não instalam SDK, banco ou IIS e não alteram o Windows.
