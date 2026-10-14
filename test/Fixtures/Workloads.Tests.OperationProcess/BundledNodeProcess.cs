// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Tests.OperationProcess;

/// <summary>
/// Exercises staged commands with actual installed workload bytes in a private process and CLI home.
/// </summary>
internal sealed class BundledNodeProcess
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        IInteractionService interaction = new SpectreInteractionService(new DefaultTheme());
        using (IHost bootstrap = await CliHostFactory.CreateHostAsync(interaction, cancellationToken))
        {
            IWorkloadInstaller installer = bootstrap.Services.GetRequiredService<IWorkloadInstaller>();
            foreach (string package in args.Skip(4))
            {
                await installer.InstallFromPackageAsync(package, cancellationToken: cancellationToken);
            }
        }

        using IHost host = await CliHostFactory.CreateHostAsync(interaction, cancellationToken);
        using IServiceScope scope = host.Services.CreateScope();
        IReadOnlyList<WorkloadEntry> installed = await host.Services.GetRequiredService<IWorkloadStore>().GetWorkloadsAsync(cancellationToken);
        TemplateConstraintEvaluationSnapshot availability = new([.. installed.Select(entry =>
            new TemplateConstraintWorkload(entry.PackageId, NuGetVersion.Parse(entry.PackageVersion), entry.Aliases, entry.Kind == WorkloadKind.Workload))]);
        string project = args[2];
        string language = args[3];
        InitExecutionCommand init = new(scope.ServiceProvider.GetRequiredService<IInitExecutionRunner>(),
            host.Services.GetRequiredService<InitExecutionRenderer>(), availability);
        string[] initArgs = [project, "--template", "basic", "--stack", "node", "--language", language, "--name", "bundled-node-" + language];
        int preview = await init.ParseInvocation([.. initArgs, "--dry-run"]).InvokeAsync(cancellationToken: cancellationToken);
        if (preview != 0 || Directory.Exists(project))
        {
            throw new InvalidOperationException("The staged project preview failed or wrote the target.");
        }

        int created = await init.ParseInvocation(initArgs).InvokeAsync(cancellationToken: cancellationToken);
        if (created != 0)
        {
            return created;
        }

        NewExecutionCommand item = new(host.Services.GetRequiredService<INewExecutionRunner>(),
            host.Services.GetRequiredService<NewExecutionRenderer>(), availability);
        string[] itemArgs = ["http", "--path", Path.Combine(project, "src", "functions"), "--name", "Greeting", "--authlevel", "anonymous"];
        int itemPreview = await item.ParseInvocation([.. itemArgs, "--dry-run"]).InvokeAsync(cancellationToken: cancellationToken);
        if (itemPreview != 0)
        {
            return itemPreview;
        }

        int itemCreated = await item.ParseInvocation(itemArgs).InvokeAsync(cancellationToken: cancellationToken);
        if (itemCreated != 0)
        {
            return itemCreated;
        }

        ProjectResolutionResult resolution = await host.Services.GetRequiredService<IFunctionsProjectResolver>()
            .ResolveProjectAsync(new(WorkingDirectory.FromExplicit(project)), cancellationToken);
        if (resolution is not ProjectResolutionResult.Resolved resolved)
        {
            throw new InvalidOperationException("The generated project was not resolved by its actual installed stack.");
        }

        await resolved.Project.PrepareForHostRunAsync(new(new(project), "node", new Dictionary<string, string>()), cancellationToken);
        interaction.WriteRawLine($"STAGED_NODE_READY:{project}");
        return 0;
    }
}
