// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Dotnet workload for the Azure Functions CLI. Provides project initialization
/// and function templates for .NET (C# and F#) Azure Functions projects using
/// the isolated worker model.
///
/// Distributed as a separate NuGet package (Azure.Functions.Cli.Workload.Dotnet)
/// and loaded at runtime via 'func workload install'.
/// </summary>
public class DotnetWorkload : IWorkload
{
    private readonly DotnetProjectInitializer _initializer;
    private readonly DotnetTemplateProvider _templateProvider;
    private readonly DotnetPackProvider _packProvider;

    public DotnetWorkload()
        : this(new DotnetCliRunner())
    {
    }

    public DotnetWorkload(IDotnetCliRunner dotnetCli)
    {
        _initializer = new DotnetProjectInitializer(dotnetCli);
        _templateProvider = new DotnetTemplateProvider(dotnetCli);
        _packProvider = new DotnetPackProvider(dotnetCli);
    }

    public string Id => "dotnet";
    public string Name => ".NET (Isolated Worker)";
    public string Description => "Azure Functions .NET support — project templates, function triggers, and build integration.";

    public void RegisterCommands(Command rootCommand)
    {
        // No additional commands for now.
    }

    public IReadOnlyList<ITemplateProvider> GetTemplateProviders() => [_templateProvider];

    public IProjectInitializer? GetProjectInitializer() => _initializer;

    public IPackProvider? GetPackProvider() => _packProvider;
}
