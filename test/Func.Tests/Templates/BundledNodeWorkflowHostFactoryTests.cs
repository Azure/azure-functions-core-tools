// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("JavaScript", "js", "httpTrigger.js")]
    [InlineData("TypeScript", "ts", "httpTrigger.ts")]
    public async Task ExplicitHostFactory_ActualInstalledNode_UsesBootCaptureAndOwnedProvidersWithNoRequestCapabilities(
        string language, string alias, string itemFile)
    {
        await InstallOwnedNodeJourneyAsync();
        NativeTemplateWorkflowHostFactory factory = new();
        using var host = await factory.CreateAsync(_interaction, _paths, new(Home), services =>
        {
            foreach (var workload in services.Where(service => service.ImplementationInstance is RuntimeWorkloadInfo)
                .Select(service => (RuntimeWorkloadInfo)service.ImplementationInstance!))
            {
                _loadContexts.Add(workload.LoadContext);
            }

            services.AddSingleton<IBasicCompanionCatalog>(_ownedJourneyCatalog!);
            services.AddSingleton<BasicAcquisitionPlanner>();
        }, enableBundleBindingConstraints: true, CancellationToken.None);
        var target = WorkingDirectory.FromExplicit(Path.Combine(_root, "explicit-host-" + alias));
        InitExecutionRequest project = new(target, "Azure.Functions.Node.Basic." + language, "ProjectName", "node", alias,
            false, true, false, true, ["--no-bundles", "true", "--skip-npm-install", "true"], new HashSet<string>());
        project.ProjectPreparation.Should().BeNull();

        using (var invocation = host.CreateInvocation())
        {
            var result = await invocation.Composer.ExecuteProjectAsync(project, CancellationToken.None);
            result.Succeeded.Should().BeTrue(string.Join(" ", result.Diagnostics));
            result.Preparation!.Completion!.Succeeded.Should().BeTrue();
            result.Preparation.Preview.Operations.Should().BeEmpty();
            result.Preparation.Preview.OwnedPlan!.Skipped.Should().BeTrue();
            result.Preparation.Completion.OwnedCompletion!.Succeeded.Should().BeTrue();
            File.Exists(Path.Combine(target.Info.FullName, ".func", "config.json")).Should().BeTrue();
        }

        var itemTarget = WorkingDirectory.FromExplicit(Path.Combine(target.Info.FullName, "src", "functions"));
        NewExecutionRequest item = new(itemTarget, "HttpTrigger-" + language, null, null,
            false, true, false, false, true, [], new HashSet<string> { "--name", "-n", "--output" });
        item.ItemCompatibility.Should().BeNull();
        item.SelectedItemEffects.Should().BeNull();
        using var itemInvocation = host.CreateInvocation();

        var created = await itemInvocation.Composer.ExecuteItemAsync(item, CancellationToken.None);

        created.Succeeded.Should().BeTrue(string.Join(" ", created.Diagnostics));
        created.ItemName!.Origin.Should().Be("authoredDefault");
        File.Exists(Path.Combine(itemTarget.Info.FullName, itemFile)).Should().BeTrue();
    }
}
