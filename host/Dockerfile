# Use the official .NET 9 SDK as a base image
FROM mcr.microsoft.com/dotnet/sdk:9.0

# Install necessary packages
RUN apt-get update && \
    apt-get install -y clang zlib1g-dev

# Set the working directory
WORKDIR /app

# Copy the source code to the container
COPY ./src .

# Publish the core tools host
RUN dotnet publish CoreToolsHost/CoreToolsHost.csproj -c Release -r linux-x64 -o ./out

# Publish DummyCLI
RUN dotnet publish DummyCli/DummyCli.csproj -c Release -r linux-x64 -o ./out/in-proc6 --self-contained

# Set the entry point to the func executable
ENTRYPOINT ["./out/func","--verbose"]
