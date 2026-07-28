# Scripts de build

`eng/build.ps1` e `eng/build.sh` verificam o SDK, limpam, restauram em locked mode, compilam Application/Infrastructure/Web Debug/Web Release/solution Release, testam e publicam Web em `artifacts/web`. Diagnósticos seguros são gerados por `eng/dotnet-diagnostics.*`.
