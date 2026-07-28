#!/usr/bin/env bash
set -euo pipefail
for pid_file in .ci-logs/web.pid .ci-logs/api.pid; do
  if [[ -f "$pid_file" ]]; then
    pid=$(cat "$pid_file")
    if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
      kill "$pid"
      for _ in $(seq 1 20); do kill -0 "$pid" 2>/dev/null || break; sleep 0.1; done
      if kill -0 "$pid" 2>/dev/null; then kill -KILL "$pid"; fi
    fi
    rm -f "$pid_file"
  fi
done
