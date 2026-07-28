#!/usr/bin/env bash
set -euo pipefail
mkdir -p .ci-logs .ci-storage
bash scripts/ci/stop-server-hosts.sh
trap 'bash scripts/ci/stop-server-hosts.sh' EXIT

dotnet run --project InovaGed.Web/InovaGed.Web.csproj --configuration Release --no-build --urls http://127.0.0.1:5080 >.ci-logs/web.log 2>&1 &
echo "$!" > .ci-logs/web.pid
dotnet run --project WebGed.WebApi/WebGed.WebApi.csproj --configuration Release --no-build --urls http://127.0.0.1:5082 >.ci-logs/api.log 2>&1 &
echo "$!" > .ci-logs/api.pid

for endpoint in http://127.0.0.1:5080/health/live http://127.0.0.1:5080/health/ready http://127.0.0.1:5082/health/live http://127.0.0.1:5082/health/ready; do
  ready=false
  for _ in $(seq 1 60); do
    for pid_file in .ci-logs/web.pid .ci-logs/api.pid; do
      pid="$(cat "$pid_file")"
      if ! kill -0 "$pid" 2>/dev/null; then
        echo "Server process $pid exited before health checks completed." >&2
        cat .ci-logs/web.log .ci-logs/api.log >&2
        exit 1
      fi
    done
    if curl --fail --silent --show-error "$endpoint" >/dev/null; then ready=true; break; fi
    sleep 1
  done
  if [[ "$ready" != true ]]; then
    echo "Host did not become ready: $endpoint" >&2
    cat .ci-logs/web.log .ci-logs/api.log >&2
    exit 1
  fi
done
