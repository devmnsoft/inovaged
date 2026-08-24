#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project InovaGed.Environment.Doctor -- database-readiness
read -r -p "Aplicar as migrations obrigatórias pendentes? [y/N] " answer
[[ "$answer" =~ ^[Yy]$ ]] && dotnet run --project InovaGed.Environment.Doctor -- apply-required-migrations
