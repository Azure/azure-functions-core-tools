ARG BASE_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:8.0

FROM ${BASE_IMAGE} AS build

WORKDIR /app

COPY NuGet.Config .
COPY src/Cli/func .
COPY tools/ tools/

ARG TARGET_RUNTIME=linux-x64
RUN dotnet publish /p:BuildNumber="9999" /p:CommitHash="N/A" -o /cli -c Release -f net8.0 --self-contained -r ${TARGET_RUNTIME}

FROM ${RUNTIME_IMAGE} AS runtime

WORKDIR /cli

COPY --from=build /cli .

# ENTRYPOINT does not expand ARGS, must use env
ARG EXECUTABLE_NAME=func
ENV EXECUTABLE_NAME=${EXECUTABLE_NAME}
ENTRYPOINT "./${EXECUTABLE_NAME}"
