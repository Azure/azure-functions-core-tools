// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

[assembly: CliWorkload<Azure.Functions.Cli.Workloads.DotNet.DotNetWorkload>()]

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Workload for .NET (C#) Azure Functions. Provides tooling and project initialization for .NET Azure Functions projects.
/// </summary>
public sealed class DotNetWorkload : Workload
{
    private const string TemplatePackageVersion = "4.0.5708";

    public override string DisplayName => ".NET Stack";

    public override string Description => "Azure Functions CLI tooling for .NET (C#) projects.";

    public override IReadOnlyList<WorkloadTemplatePackage> RequiredTemplatePackages =>
    [
        new(
            "Microsoft.Azure.Functions.Worker.ProjectTemplates",
            TemplatePackageVersion,
            Path.Combine("templates", $"microsoft.azure.functions.worker.projecttemplates.{TemplatePackageVersion}.nupkg")),
        new(
            "Microsoft.Azure.Functions.Worker.ItemTemplates",
            TemplatePackageVersion,
            Path.Combine("templates", $"microsoft.azure.functions.worker.itemtemplates.{TemplatePackageVersion}.nupkg")),
    ];

    public override void Configure(FunctionsCliBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IDotnetPathResolver, DotnetPathResolver>();
        builder.Services.AddSingleton<IDotnetCliRunner, DotnetCliRunner>();
        builder.Services.AddSingleton<ITemplateHivePathProvider, TemplateHivePathProvider>();
        builder.Services.AddSingleton<DotNetProjectStack>();
        builder.Services.AddSingleton<IProjectStack>(sp => sp.GetRequiredService<DotNetProjectStack>());
        builder.Services.AddSingleton<IProjectInitializer, DotNetProjectInitializer>();
        builder.Services.AddSingleton<IStartHostOptionContributor, DotNetStartOptionContributor>();
        builder.Services.AddSingleton<IQuickstartProvider, DotNetQuickstartProvider>();
        builder.AddProjectFactory<DotNetProjectFactory>();
    }
}
