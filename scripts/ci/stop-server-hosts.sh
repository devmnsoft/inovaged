#!/usr/bin/env bash
set -euo pipefail
for pid_file in .ci-logs/web.pid .ci-logs/api.pid; do
  if [[ -f "$pid_file" ]]; then
    pid=$(cat "$pid_file")
    if kill -0 "$pid" 2>/dev/null; then kill "$pid"; fi
  fi
done
