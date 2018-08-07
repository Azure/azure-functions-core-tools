#!/usr/bin/env bash
dotnet build Azure.Functions.Cli.sln
dotnet publish src/Azure.Functions.Cli/Azure.Functions.Cli.csproj --runtime linux-x64 --output /tmp/cli
# FUNC_PATH=/tmp/cli/func dotnet test test/Azure.Functions.Cli.Tests/Azure.Functions.Cli.Tests.csproj
