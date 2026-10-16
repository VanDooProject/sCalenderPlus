#!/bin/sh
# Entry point of the web image (web.Dockerfile): refuses to start when the deployment expects the
# web image of another edition, then runs Caddy.
#
#   SCAL_WEB_IMAGE      this image's name, set at build time from EDITION
#                       (scalenderplus-web or scalenderplus-poly-web)
#   EXPECTED_WEB_IMAGE  the name the deployment asked for (compose: WEB_IMAGE); unset = no check
#
# A local `docker compose build` with WEB_IMAGE=scalenderplus-poly-web but EDITION unset would
# otherwise tag a standard build as the poly image (docs/deployment/coolify.md §3).
set -eu

if [ -n "${EXPECTED_WEB_IMAGE:-}" ] && [ "$EXPECTED_WEB_IMAGE" != "$SCAL_WEB_IMAGE" ]; then
  echo "web: this image is $SCAL_WEB_IMAGE (EDITION=$SCAL_EDITION), but WEB_IMAGE is" \
    "$EXPECTED_WEB_IMAGE. Set WEB_IMAGE and EDITION as a pair (scalenderplus-web + standard," \
    "scalenderplus-poly-web + poly) and rebuild; see deploy/.env.example." >&2
  exit 64
fi

exec caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
