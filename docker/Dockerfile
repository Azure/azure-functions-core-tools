ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:8.0

FROM ${SDK_IMAGE} AS build

WORKDIR /app

COPY *.props .
COPY stylecop.json .
COPY .editorconfig .
COPY NuGet.Config .
COPY tools/ tools/
COPY eng/*.props eng/
COPY src/*.props src/
COPY src/Cli/func src/Cli/func
COPY AzureFunctions-CLI.ico .
COPY eng/scripts/download-templates.ps1 .

ARG TARGET_RUNTIME=linux-x64
RUN dotnet publish src/Cli/func /p:BuildNumber="9999" /p:CommitHash="N/A" -o /cli -c Release -f net8.0 --self-contained -r ${TARGET_RUNTIME}

RUN pwsh ./download-templates.ps1 -OutputPath "/cli"

FROM ${SDK_IMAGE} AS runtime

WORKDIR /cli

COPY --from=build /cli .

# Add cli to path so it can be run from anywhere
ENV PATH="$PATH:/cli"

WORKDIR /playground

# Run forever
ENTRYPOINT ["tail", "-f", "/dev/null"]