#!/usr/bin/env bash
dotnet build Azure.Functions.Cli.sln
dotnet test test/Azure.Functions.Cli.Tests/Azure.Functions.Cli.Tests.csproj