#!/usr/bin/env bash
set -euo pipefail

actionlint -verbose -color .github/workflows/*.yml
