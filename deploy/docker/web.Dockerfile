# sCalenderPlus web image: Caddy serving the built Vue SPA and reverse-proxying the api (same origin).
# Build context: repository root. EDITION selects the build-time edition (frontend/app/src/editions):
#   docker build -f deploy/docker/web.Dockerfile -t scalenderplus-web .
#   docker build -f deploy/docker/web.Dockerfile --build-arg EDITION=poly -t scalenderplus-poly-web .
# The image knows its name (SCAL_WEB_IMAGE, OCI title) from EDITION and refuses to start when the
# deployment expects another one (EXPECTED_WEB_IMAGE, set by the compose files from WEB_IMAGE), so a
# local build with a WEB_IMAGE/EDITION mismatch fails fast instead of serving the wrong edition.
# An unknown EDITION fails the build (no `edition-<name>` stage).
ARG EDITION=standard

FROM --platform=$BUILDPLATFORM node:22-alpine AS build
WORKDIR /src/frontend
ENV PNPM_HOME=/pnpm CI=true
# pnpm version comes from `packageManager` in frontend/package.json.
RUN corepack enable

# Install first (cached layer): manifests and lockfile only.
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/pnpm-workspace.yaml ./
COPY frontend/app/package.json app/
COPY frontend/packages/api-client/package.json packages/api-client/
COPY frontend/packages/ui/package.json packages/ui/
RUN --mount=type=cache,id=pnpm-store,target=/pnpm/store \
    pnpm install --frozen-lockfile --store-dir /pnpm/store --filter app...

COPY frontend/ ./
ARG EDITION
RUN VITE_EDITION="$EDITION" pnpm --filter app build

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

COPY deploy/caddy/web.Caddyfile /etc/caddy/Caddyfile
COPY deploy/caddy/config.json.tmpl /srv/config.json
COPY deploy/caddy/manifest.webmanifest.tmpl /srv/manifest.webmanifest.tmpl
COPY --chmod=0555 deploy/docker/web-start.sh /usr/local/bin/web-start

# The image name per edition (docs/product/editions.md §2.3); only the selected stage is built.
FROM runtime-base AS edition-standard
ENV SCAL_WEB_IMAGE=scalenderplus-web

FROM runtime-base AS edition-poly
ENV SCAL_WEB_IMAGE=scalenderplus-poly-web

FROM edition-${EDITION} AS runtime
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ARG EDITION
LABEL org.opencontainers.image.title="${SCAL_WEB_IMAGE}" \
      org.opencontainers.image.description="sCalenderPlus web app (Caddy: SPA + same-origin reverse proxy to the api)" \
      org.opencontainers.image.source="https://github.com/VanDooProject/sCalenderPlus" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.vandooproject.scalenderplus.edition="${EDITION}"
COPY --from=build /src/frontend/app/dist/ /srv/

ENV WEB_PORT=8080 \
    API_UPSTREAM=api:8080 \
    PUBLIC_ENVIRONMENT=production \
    SCAL_EDITION=${EDITION}
USER 10001:10001
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
  CMD ["wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/healthz"]
CMD ["/usr/local/bin/web-start"]
