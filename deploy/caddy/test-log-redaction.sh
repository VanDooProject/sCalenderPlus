#!/bin/sh
# Log capture test of the web proxy (docs/architecture/ical-caldav.md §1.1): requests to iCal feed URLs —
# also double-slash and percent-encoded spellings, with the secret in the Referer or the query — must leave no
# feed secret in Caddy's access or error log. The api's side is FeedHostingTests in the backend suite.
#
#   deploy/caddy/test-log-redaction.sh [IMAGE]
#
# IMAGE: a built web image (CI: the docker job). Without it, caddy:2-alpine runs this directory's Caddyfile.
# Needs docker and curl. The api upstream points at a closed port, so proxied requests fail with 502 and
# the error log (which carries the full request) is exercised too.
set -eu

dir=$(cd "$(dirname "$0")" && pwd)
image=${1:-}
name="scal-caddy-redaction-$$"
secret="Zf8kT2qLx9VbN4mR7cWy1aHe5uJd3sGp6oQi0tKzXvB" # 43 characters, like a real token
link_secret="Lk2nB7xQ4wE9rT1yU6iO3pA8sD5fG0hJ2kL4zX7cV9m"

cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; }
trap cleanup EXIT

if [ -n "$image" ]; then
  docker run -d --name "$name" -p 127.0.0.1::8080 -e API_UPSTREAM=127.0.0.1:9 "$image" >/dev/null
else
  docker run -d --name "$name" -p 127.0.0.1::8080 -e API_UPSTREAM=127.0.0.1:9 -e WEB_PORT=8080 \
    -v "$dir/web.Caddyfile:/etc/caddy/Caddyfile:ro" -v "$dir/config.json.tmpl:/srv/config.json:ro" \
    caddy:2-alpine caddy run --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null
fi

port=$(docker port "$name" 8080/tcp | head -n 1 | sed 's/.*://')
base="http://127.0.0.1:$port"
i=0
until curl -fsS "$base/healthz" >/dev/null 2>&1; do
  i=$((i + 1))
  if [ "$i" -gt 50 ]; then
    echo "web did not become ready" >&2
    docker logs "$name" >&2
    exit 1
  fi
  sleep 0.2
done

for path in \
  "/ical/v1/$secret.ics" \
  "/ical/v1/s/$link_secret.ics" \
  "//ical/v1/$secret.ics" \
  "/%69cal/v1/$secret.ics" \
  "/ical%2Fv1/$secret.ics" \
  "/ical%5Cv1/$secret.ics" \
  "/ical\\v1/$secret.ics" \
  "/ical/v1/x.ics?token=$secret"; do
  curl -s -o /dev/null -H "Referer: https://app.example.test/ical/v1/s/$link_secret.ics" "$base$path" || true
done
# A same-origin page whose Referer carries a secret (e.g. a feed opened in the browser, then the app).
curl -s -o /dev/null -H "Referer: https://app.example.test/ical/v1/$secret.ics" "$base/config.json" || true

sleep 1
logs=$(docker logs "$name" 2>&1)

if ! printf '%s\n' "$logs" | grep -q '/ical/\[REDACTED\]'; then
  echo "FAIL: no redacted /ical/ request in the logs (is access logging on?)" >&2
  printf '%s\n' "$logs" >&2
  exit 1
fi

for s in "$secret" "$link_secret"; do
  if printf '%s\n' "$logs" | grep -q "$s"; then
    echo "FAIL: a feed secret appears in the web proxy's logs:" >&2
    printf '%s\n' "$logs" | grep "$s" >&2
    exit 1
  fi
done

echo "ok: $(printf '%s\n' "$logs" | grep -c 'REDACTED') log lines redacted, no feed secret logged"
