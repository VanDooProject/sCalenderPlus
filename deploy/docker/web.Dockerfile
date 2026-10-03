# sCalenderPlus web image: Caddy serving the built Vue SPA and reverse-proxying the api (same origin).
# Build context: repository root.
#   docker build -f deploy/docker/web.Dockerfile -t scalenderplus-web .

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
RUN pnpm --filter app build

FROM caddy:2-alpine AS runtime
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
LABEL org.opencontainers.image.title="scalenderplus-web" \
      org.opencontainers.image.description="sCalenderPlus web app (Caddy: SPA + same-origin reverse proxy to the api)" \
      org.opencontainers.image.source="https://github.com/VanDooProject/sCalenderPlus" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"

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
COPY --from=build /src/frontend/app/dist/ /srv/

ENV WEB_PORT=8080 \
    API_UPSTREAM=api:8080 \
    PUBLIC_ENVIRONMENT=production
USER 10001:10001
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=5s --retries=3 \
  CMD ["wget", "-q", "-O", "/dev/null", "http://127.0.0.1:8080/healthz"]
CMD ["caddy", "run", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"]
