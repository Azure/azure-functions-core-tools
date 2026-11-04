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
COPY package*.json ./
RUN npm install --omit=dev

# === BYOC: RUNTIME STAGE ===
FROM {{RUNTIME_IMAGE}} AS runtime
WORKDIR /home/site/wwwroot

# === BYOC: RUNTIME PACKAGES - customer customization ===
# Add native runtime dependencies here.

# === BYOC: COPY APPLICATION ===
COPY . ./
COPY --from=build /src/node_modules/ ./node_modules/
COPY --from=worker-source /worker-packages/{{WORKER_PACKAGE_CONTENT_PATH}}/ /azure-functions-worker/

# === BYOC: PERMISSIONS - customer customization ===
RUN chown -R node:node /home/site/wwwroot

# === BYOC: STARTUP SCRIPT ===
COPY entrypoint.sh /usr/local/bin/byoc-entrypoint
RUN chmod 755 /usr/local/bin/byoc-entrypoint
USER node
ENTRYPOINT ["/usr/local/bin/byoc-entrypoint"]
