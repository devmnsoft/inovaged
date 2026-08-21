#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet clean InovaGed.sln
dotnet restore InovaGed.sln
dotnet build InovaGed.sln -v:minimal
dotnet run --project InovaGed.Environment.Doctor --no-build -- quality-gate "$@"
