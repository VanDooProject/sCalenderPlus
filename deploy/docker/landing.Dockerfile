# sCalenderPlus landing image: Caddy serving the prerendered marketing site (frontend/landing, vite-ssg).
# Build context: repository root. EDITION selects the build-time edition (frontend/landing/src/editions):
#   docker build -f deploy/docker/landing.Dockerfile -t scalenderplus-landing .
#   docker build -f deploy/docker/landing.Dockerfile --build-arg EDITION=poly -t scalenderplus-poly-landing .
# The pages are static HTML with absolute URLs (canonical, hreflang, Open Graph, sitemap), so the site
# settings are build args, not container environment: a white-label deployment builds its own image
# (docs/deployment/coolify.md §13). All are optional; see frontend/landing/src/lib/siteConfig.ts.
# An unknown EDITION fails the build (no `edition-<name>` stage).
ARG EDITION=standard

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src/frontend
ENV PNPM_HOME=/pnpm CI=true
# pnpm version comes from `packageManager` in frontend/package.json.
RUN corepack enable

# Install first (cached layer): manifests and lockfile only. The edition leak check lives in the app
# package (app/scripts) and needs the workspace root's TypeScript, hence the root (`.`) in the filter.
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./
COPY frontend/landing/package.json landing/
COPY frontend/packages/plans/package.json packages/plans/
COPY frontend/packages/ui/package.json packages/ui/
RUN --mount=type=cache,id=pnpm-store,target=/pnpm/store \
    pnpm install --frozen-lockfile --store-dir /pnpm/store --filter landing... --filter .

COPY frontend/ ./
ARG EDITION
ARG SITE_URL=""
ARG APP_URL=""
ARG BRAND_NAME=""
ARG BRAND_HUE=""
ARG SUPPORT_EMAIL=""
ARG IMPRINT_URL=""
ARG PRIVACY_URL=""
ARG TERMS_URL=""
ARG DPA_URL=""
ARG BOT_USER_AGENT=""
# The leak check runs on exactly the files this image ships (docs/product/editions.md §2.2).
RUN VITE_EDITION="$EDITION" VITE_SITE_URL="$SITE_URL" VITE_APP_URL="$APP_URL" \
    VITE_BRAND_NAME="$BRAND_NAME" VITE_BRAND_HUE="$BRAND_HUE" VITE_SUPPORT_EMAIL="$SUPPORT_EMAIL" \
    VITE_IMPRINT_URL="$IMPRINT_URL" VITE_PRIVACY_URL="$PRIVACY_URL" VITE_TERMS_URL="$TERMS_URL" \
    VITE_DPA_URL="$DPA_URL" VITE_BOT_USER_AGENT="$BOT_USER_AGENT" \
    pnpm --filter landing build \
    && pnpm --filter landing check:edition-leak --edition "$EDITION"

FROM caddy:2-alpine AS runtime-base

# Non-root: listen on 8080 and drop the file capability (cap_net_bind_service) the base image sets on
# the binary, so the container also starts with `no-new-privileges` and all capabilities dropped.
RUN apk add --no-cache libcap \
    && setcap -r /usr/bin/caddy \
    && apk del libcap \
    && addgroup -S -g 10001 caddy \
    && adduser -S -D -H -u 10001 -G caddy caddy \
    && mkdir -p /data /config \
    && chown caddy:caddy /data /config

COPY deploy/caddy/landing.Caddyfile /etc/caddy/Caddyfile

# The image name per edition (as web.Dockerfile); only the selected stage is built.
FROM runtime-base AS edition-standard
ENV SCAL_LANDING_IMAGE=scalenderplus-landing

FROM runtime-base AS edition-poly
ENV SCAL_LANDING_IMAGE=scalenderplus-poly-landing

FROM edition-${EDITION} AS runtime
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ARG EDITION
LABEL org.opencontainers.image.title="${SCAL_LANDING_IMAGE}" \
      org.opencontainers.image.description="sCalenderPlus landing page (Caddy serving the prerendered marketing site)" \
      org.opencontainers.image.source="https://github.com/VanDooProject/sCalenderPlus" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.vandooproject.scalenderplus.edition="${EDITION}"
COPY --from=build /src/frontend/landing/dist/ /srv/

ENV LANDING_PORT=8080 \
    SCAL_EDITION=${EDITION}
USER 10001:10001
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
  CMD ["wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/healthz"]
CMD ["caddy", "run", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"]
