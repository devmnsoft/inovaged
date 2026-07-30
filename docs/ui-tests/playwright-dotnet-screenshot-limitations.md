# Limitações de screenshot no Playwright .NET

O hotfix 04.1.27-R3 remove a tentativa de usar `expect(page).toHaveScreenshot()`, uma asserção do runner JavaScript/TypeScript `@playwright/test`. A versão .NET adotada pelo repositório não expõe essa operação em `IPageAssertions`; a integração xUnit também não altera esse contrato.

A suíte continua usando o pacote `Microsoft.Playwright` e o ciclo de vida manual do Chromium. Ela captura bytes reais com `IPage.ScreenshotAsync`, página inteira e animações desabilitadas, e delega a verificação ao comparador .NET do repositório.

A estabilização do DOM usa `IPage.EvaluateAsync`, pois não há valor a devolver ao C#. Isso evita a inferência genérica exigida por `ILocator.EvaluateAllAsync<T>`.
