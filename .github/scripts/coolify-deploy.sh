#!/usr/bin/env bash
# Deploy a Coolify Docker Compose application and wait until it is healthy
# (docs/deployment/coolify.md §11). Used by the `deploy-staging` job in .github/workflows/images.yml;
# also runnable by hand with the same environment variables.
#
#   COOLIFY_WEBHOOK_URL  Deploy webhook of the application, as shown in Coolify (Webhooks tab):
#                        https://<coolify>/api/v1/deploy?uuid=<application-uuid>&force=false
#   COOLIFY_TOKEN        Coolify API token. `deploy` is required; `read` lets this script follow the
#                        deployment; `write` lets it pin IMAGE_TAG. Missing permissions are skipped with a warning.
#   IMAGE_TAG            Optional. Image tag to pin in the application's IMAGE_TAG variable (e.g. main-<sha>).
#   HEALTH_URL           Optional. URL polled after the deployment until it answers 200 (…/health/ready).
#   DEPLOY_TIMEOUT       Seconds to wait for the Coolify deployment (default 900).
#   HEALTH_TIMEOUT       Seconds to wait for HEALTH_URL (default 300).
#
# Without COOLIFY_WEBHOOK_URL or COOLIFY_TOKEN the script prints a notice and exits 0.
set -euo pipefail

notice() { echo "::notice::$*"; }
warn() { echo "::warning::$*"; }
fail() { echo "::error::$*"; exit 1; }
summary() { if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then echo "$*" >> "$GITHUB_STEP_SUMMARY"; fi; }

if [ -z "${COOLIFY_WEBHOOK_URL:-}" ] || [ -z "${COOLIFY_TOKEN:-}" ]; then
  notice "COOLIFY_WEBHOOK_URL or COOLIFY_TOKEN is not set: skipping the Coolify deployment."
  summary "Coolify deployment skipped: secrets COOLIFY_WEBHOOK_URL / COOLIFY_TOKEN not configured."
  exit 0
fi

DEPLOY_TIMEOUT="${DEPLOY_TIMEOUT:-900}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-300}"
AUTH_HEADER="Authorization: Bearer ${COOLIFY_TOKEN}"

case "$COOLIFY_WEBHOOK_URL" in
  http://*/api/v1/deploy\?* | https://*/api/v1/deploy\?*) ;;
  *) fail "COOLIFY_WEBHOOK_URL must look like https://<coolify>/api/v1/deploy?uuid=<uuid>" ;;
esac
API_BASE="${COOLIFY_WEBHOOK_URL%%/api/v1/deploy*}/api/v1"
APP_UUID="$(sed -nE 's/.*[?&]uuid=([^&]+).*/\1/p' <<< "$COOLIFY_WEBHOOK_URL")"
[ -n "$APP_UUID" ] || fail "COOLIFY_WEBHOOK_URL has no uuid=<application-uuid> parameter"

# api METHOD URL [JSON]: response body in $RESPONSE, status code in $HTTP_STATUS (run in this shell, not in
# a command substitution). Never prints the token.
BODY_FILE="$(mktemp)"
trap 'rm -f "$BODY_FILE"' EXIT
api() {
  local method="$1" url="$2" data="${3:-}"
  local args=(-sS -o "$BODY_FILE" -w '%{http_code}' -X "$method" -H "$AUTH_HEADER" -H 'Accept: application/json')
  if [ -n "$data" ]; then
    args+=(-H 'Content-Type: application/json' --data "$data")
  fi
  : > "$BODY_FILE"
  HTTP_STATUS="$(curl "${args[@]}" "$url" || true)"
  HTTP_STATUS="${HTTP_STATUS:-000}"
  RESPONSE="$(cat "$BODY_FILE")"
}
message() { jq -r '.deployments[0].message? // .message? // empty' <<< "$RESPONSE" 2>/dev/null || true; }

# 1. Pin the image tag (needs `write`); otherwise Coolify deploys the IMAGE_TAG configured in its UI
#    (default `main`, pulled on every deployment).
if [ -n "${IMAGE_TAG:-}" ]; then
  body="$(jq -nc --arg v "$IMAGE_TAG" '{key: "IMAGE_TAG", value: $v}')"
  api PATCH "$API_BASE/applications/$APP_UUID/envs" "$body"
  if [[ "$HTTP_STATUS" == 2* ]]; then
    echo "Pinned IMAGE_TAG=$IMAGE_TAG"
  else
    warn "Could not pin IMAGE_TAG=$IMAGE_TAG (HTTP $HTTP_STATUS: $(message)). Deploying the IMAGE_TAG configured in Coolify instead."
  fi
fi

# 2. Trigger the deployment (POST since Coolify 4.3; older versions accept GET only).
api POST "$COOLIFY_WEBHOOK_URL"
if [ "$HTTP_STATUS" = 405 ]; then
  api GET "$COOLIFY_WEBHOOK_URL"
fi
[[ "$HTTP_STATUS" == 2* ]] || fail "Coolify deploy request failed (HTTP $HTTP_STATUS): $(message)"
deployment_uuid="$(jq -r '.deployments[0].deployment_uuid? // empty' <<< "$RESPONSE" 2>/dev/null || true)"
echo "Coolify: $(message)"

# 3. Follow the deployment (needs `read`).
status=unknown
if [ -n "$deployment_uuid" ]; then
  deadline=$((SECONDS + DEPLOY_TIMEOUT))
  while [ "$SECONDS" -lt "$deadline" ]; do
    api GET "$API_BASE/deployments/$deployment_uuid"
    if [ "$HTTP_STATUS" = 401 ] || [ "$HTTP_STATUS" = 403 ]; then
      warn "Token cannot read deployments (HTTP $HTTP_STATUS): not following deployment $deployment_uuid."
      status=unknown
      break
    fi
    status="$(jq -r '.status? // "unknown"' <<< "$RESPONSE" 2>/dev/null || echo unknown)"
    case "$status" in
      finished) break ;;
      failed | cancelled*) fail "Coolify deployment $deployment_uuid ended with status '$status' (see the Coolify deployment log)." ;;
    esac
    sleep 10
  done
  if [ "$status" = queued ] || [ "$status" = in_progress ]; then
    fail "Coolify deployment $deployment_uuid still '$status' after ${DEPLOY_TIMEOUT}s."
  fi
  echo "Coolify deployment $deployment_uuid: $status"
else
  warn "Coolify returned no deployment_uuid (deployment already queued?)."
fi

# 4. Wait until the public health endpoint is ready.
if [ -z "${HEALTH_URL:-}" ]; then
  notice "HEALTH_URL is not set (repository variable STAGING_URL): skipping the health check."
  summary "Deployed to Coolify (deployment status: $status). Health check skipped: STAGING_URL not set."
  exit 0
fi
if [ "$status" != finished ]; then
  # Without the deployment status the old containers may still answer; give the deployment a head start.
  sleep 60
fi
deadline=$((SECONDS + HEALTH_TIMEOUT))
code=000
while [ "$SECONDS" -lt "$deadline" ]; do
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "$HEALTH_URL" || echo 000)"
  [ "$code" = 200 ] && break
  sleep 5
done
[ "$code" = 200 ] || fail "$HEALTH_URL did not return 200 within ${HEALTH_TIMEOUT}s (last: $code)."
echo "$HEALTH_URL is ready."
summary "Deployed to Coolify (deployment status: $status); \`$HEALTH_URL\` returned 200."
