# sCalenderPlus worker image (docs/deployment/coolify.md §2). Build context: repository root.
#   docker build -f deploy/docker/worker.Dockerfile -t scalenderplus-worker .
# Framework-dependent, RID-neutral publish (no apphost): the build stage runs natively on the build
# platform and the output runs on any target architecture of the runtime image (linux/amd64, linux/arm64).

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true

# Restore first (cached layer): only project files, lock files and MSBuild/SDK settings.
# No `--mount=type=cache` for ~/.nuget/packages: the restored packages must live in this layer, because
# CI's layer cache (cache-to: type=gha) restores the layer but not cache-mount contents, and the
# `--no-restore` publish below would then find an empty package folder (NETSDK1064).
COPY global.json .editorconfig ./
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/SCalenderPlus.Core/*.csproj backend/src/SCalenderPlus.Core/packages.lock.json backend/src/SCalenderPlus.Core/
COPY backend/src/SCalenderPlus.Application/*.csproj backend/src/SCalenderPlus.Application/packages.lock.json backend/src/SCalenderPlus.Application/
COPY backend/src/SCalenderPlus.Infrastructure/*.csproj backend/src/SCalenderPlus.Infrastructure/packages.lock.json backend/src/SCalenderPlus.Infrastructure/
COPY backend/src/SCalenderPlus.Worker/*.csproj backend/src/SCalenderPlus.Worker/packages.lock.json backend/src/SCalenderPlus.Worker/
RUN dotnet restore backend/src/SCalenderPlus.Worker/SCalenderPlus.Worker.csproj --locked-mode

COPY backend/src/ backend/src/
# OpenApiGenerateDocuments=false: the committed backend/openapi/v1.json is not part of the image.
RUN dotnet publish backend/src/SCalenderPlus.Worker/SCalenderPlus.Worker.csproj --no-restore --configuration Release --output /app \
      -p:UseAppHost=false -p:OpenApiGenerateDocuments=false

# Chiseled Ubuntu: no shell or package manager, runs as the non-root `app` user (UID 1654).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
LABEL org.opencontainers.image.title="scalenderplus-worker" \
      org.opencontainers.image.description="sCalenderPlus background job worker" \
      org.opencontainers.image.source="https://github.com/VanDooProject/sCalenderPlus" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}"
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8081
COPY --from=build --chown=root:root /app ./
USER $APP_UID
EXPOSE 8081
# Built-in probe (no curl in chiseled images): GET /health/ready on localhost, exit 0/1.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD ["dotnet", "SCalenderPlus.Worker.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "SCalenderPlus.Worker.dll"]
