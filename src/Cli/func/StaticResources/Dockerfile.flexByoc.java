# === BYOC: WORKER SOURCE ===
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS worker-source
RUN dotnet new classlib --name WorkerRestore --output /tmp/worker-restore --no-restore \
    && NUGET_PACKAGES=/worker-packages dotnet add /tmp/worker-restore/WorkerRestore.csproj package {{WORKER_PACKAGE_ID}} \
        --version {{WORKER_PACKAGE_VERSION}} \
        --source {{WORKER_PACKAGE_FEED}}

# === BYOC: BUILD STAGE ===
FROM {{BUILD_IMAGE}} AS build
WORKDIR /src

# === BYOC: BUILD PACKAGES - customer customization ===
# Add build-only tools here.

# === BYOC: BUILD PROJECT ===
COPY pom.xml ./
RUN mvn dependency:go-offline
COPY . ./
RUN mvn clean package \
    && mkdir -p /publish \
    && cd target/azure-functions \
    && function_app="$(find . -mindepth 1 -maxdepth 1 -type d | head -n 1)" \
    && test -n "$function_app" \
    && cp -a "$function_app"/. /publish/

# === BYOC: RUNTIME STAGE ===
FROM {{RUNTIME_IMAGE}} AS runtime
WORKDIR /home/site/wwwroot

# === BYOC: RUNTIME PACKAGES - customer customization ===
# Add native runtime dependencies here.

# === BYOC: COPY APPLICATION ===
COPY --from=build /publish/ ./
COPY --from=worker-source /worker-packages/{{WORKER_PACKAGE_CONTENT_PATH}}/ /azure-functions-worker/

# === BYOC: PERMISSIONS - customer customization ===
RUN groupadd --gid 1000 app \
    && useradd --uid 1000 --gid app --create-home app \
    && chown -R app:app /home/site/wwwroot

# === BYOC: STARTUP SCRIPT ===
COPY entrypoint.sh /usr/local/bin/byoc-entrypoint
RUN chmod 755 /usr/local/bin/byoc-entrypoint
USER app
ENTRYPOINT ["/usr/local/bin/byoc-entrypoint"]
