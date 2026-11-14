# === BYOC: WORKER SOURCE ===
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS worker-source
RUN dotnet new classlib --name WorkerRestore --output /tmp/worker-restore --no-restore \
    && NUGET_PACKAGES=/worker-packages dotnet add /tmp/worker-restore/WorkerRestore.csproj package {{WORKER_PACKAGE_ID}} \
        --version {{WORKER_PACKAGE_VERSION}} \
        --source {{WORKER_PACKAGE_FEED}}

# === BYOC: RUNTIME STAGE ===
FROM {{RUNTIME_IMAGE}} AS runtime
ENV FUNCTIONS_WORKER_RUNTIME_VERSION={{RUNTIME_VERSION}}
WORKDIR /home/site/wwwroot

# === BYOC: RUNTIME PACKAGES - customer customization ===
# Add native runtime dependencies here.

# === BYOC: MANAGED DEPENDENCIES - open question ===
# The installation contract for modules declared in requirements.psd1 is not yet defined.

# === BYOC: COPY APPLICATION ===
COPY . ./
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
