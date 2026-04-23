// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Stub initializer that demonstrates the extension shape: contributes a
/// <c>--target-framework</c> option to <c>func init</c>, then writes a placeholder
/// .csproj and host.json. Replaced by the full dotnet implementation later.
/// </summary>
public sealed class DotnetProjectInitializer : IProjectInitializer
{
    public static readonly Option<string> TargetFrameworkOption = new("--target-framework")
    {
        Description = "[dotnet] The .NET target framework moniker (e.g. net10.0).",
        DefaultValueFactory = _ => "net10.0",
    };

    public string WorkerRuntime => "dotnet";

    public IReadOnlyList<string> SupportedLanguages => ["C#", "F#"];

    public bool CanHandle(string workerRuntime) =>
        workerRuntime.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
        workerRuntime.Equals("dotnet-isolated", StringComparison.OrdinalIgnoreCase) ||
        workerRuntime.Equals("csharp", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<Option> GetInitOptions() => [TargetFrameworkOption];

    public async Task InitializeAsync(
        ProjectInitContext context,
        ParseResult parseResult,
        CancellationToken cancellationToken = default)
    {
        var tfm = parseResult.GetValue(TargetFrameworkOption) ?? "net10.0";
        var projectName = context.ProjectName ?? Path.GetFileName(context.ProjectPath.TrimEnd(Path.DirectorySeparatorChar));

        Directory.CreateDirectory(context.ProjectPath);

        var csproj = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{tfm}</TargetFramework>
                <AzureFunctionsVersion>v4</AzureFunctionsVersion>
              </PropertyGroup>
            </Project>
            """;

        await File.WriteAllTextAsync(
            Path.Combine(context.ProjectPath, $"{projectName}.csproj"),
            csproj,
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(context.ProjectPath, "host.json"),
            """{ "version": "2.0" }""",
            cancellationToken);
    }
}
