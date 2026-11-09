#!/usr/bin/env bash
set -euo pipefail

# TurnOva MVP CI validates a native .NET publish only. Container build and
# container-hosting manifests are explicitly outside the approved scope.
unexpected="$(
  find . -type f \
    \( -iname 'Dockerfile' -o -iname '.dockerignore' -o -iname 'docker-compose.yml' -o -iname 'docker-compose.yaml' \) \
    -not -path './.git/*' -print -quit
)"
if [[ -n "$unexpected" ]]; then
  echo "Container build file is not permitted in the TurnOva MVP: $unexpected" >&2
  exit 1
fi

if [[ -f render.yaml ]] && grep -Eiq 'runtime:[[:space:]]*docker|dockerfilePath:|dockerContext:' render.yaml; then
  echo "Container-based Render hosting is not permitted for the TurnOva MVP." >&2
  exit 1
fi

if grep -R -nE 'docker build|docker push|uses:[[:space:]]+docker/|runtime:[[:space:]]*docker|dockerfilePath:' .github/workflows; then
  echo "Container build/deployment step detected in GitHub Actions." >&2
  exit 1
fi

echo "Native-only hosting policy verified; no container build/deployment configuration found."
