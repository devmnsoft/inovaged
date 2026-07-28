# Toolchain oficial

InovaGED usa SDK estável .NET 8, runtime .NET 8, C# 12 e `TargetFramework` explícito `net8.0`. `global.json` parte de 8.0.100 com `latestFeature`, permitindo feature bands 8.0 estáveis, recusando previews e sem avançar para SDK 9/10. Execute `eng/verify-dotnet-sdk.*` antes do restore.
