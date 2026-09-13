#!/usr/bin/env bash
# Deploys ZenlessLeague.Api to Cloud Run (service "zenless-league-api", region europe-west1).
#
# One-time setup: copy env-vars.example.yaml to env-vars.yaml (same directory) and fill in real
# values -- that file is gitignored, never committed, same convention as the Firebase service
# account file under src/ZenlessLeague.Api/secrets/. Requires `gcloud` authenticated
# (`gcloud auth login`) against an account with deploy access to the zzz-league GCP project.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_VARS_FILE="$SCRIPT_DIR/env-vars.yaml"

if [[ ! -f "$ENV_VARS_FILE" ]]; then
	echo "Missing $ENV_VARS_FILE" >&2
	echo "Copy deploy/env-vars.example.yaml to deploy/env-vars.yaml and fill in real values (never committed -- see .gitignore)." >&2
	exit 1
fi

cd "$REPO_ROOT"
gcloud run deploy zenless-league-api \
	--source . \
	--region europe-west1 \
	--allow-unauthenticated \
	--min-instances=1 \
	--env-vars-file="$ENV_VARS_FILE" \
	--quiet
