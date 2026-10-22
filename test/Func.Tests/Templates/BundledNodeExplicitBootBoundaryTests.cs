// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Node;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Node;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    [Theory]
    [InlineData("JavaScript", "js", ".js", "src/functions/*.js")]
    [InlineData("TypeScript", "ts", ".ts", "dist/src/functions/*.js")]
    public async Task ExplicitBootBoundary_RootAndNestedNew_KeepRequestedTargetsWithoutClaimingLegacyRootPlacementParity(
        string language, string alias, string extension, string main)
    {
        await _installer.InstallFromPackageAsync(BuiltArchive);
        using var host = await new NativeTemplateWorkflowHostFactory().CreateAsync(_interaction, _paths, new(Home), services =>
        {
            _loadContexts.AddRange(services.Select(service => service.ImplementationInstance).OfType<RuntimeWorkloadInfo>()
                .Select(workload => workload.LoadContext));
            services.AddScoped<NodeNpmInstaller>();
            services.AddScoped<NodeTemplateWorkflowProvider>();
            services.AddScoped<INativeItemWorkflowProvider>(provider => provider.GetRequiredService<NodeTemplateWorkflowProvider>());
            services.AddScoped<INativeProjectWorkflowProvider>(provider => provider.GetRequiredService<NodeTemplateWorkflowProvider>());
        }, enableBundleBindingConstraints: true, CancellationToken.None);
        var project = WorkingDirectory.FromExplicit(Path.Combine(_root, "boot-boundary-" + alias));
        var init = NodeComposedInit(project.Info.FullName, alias, ["--no-bundles", "true", "--skip-npm-install", "true"]);
        init.ProjectPreparation.Should().BeNull();

        using (var invocation = host.CreateInvocation())
        {
            var result = await invocation.Composer.ExecuteProjectAsync(init, CancellationToken.None);
            result.Succeeded.Should().BeTrue(string.Join("; ", result.Diagnostics));
            result.Preparation!.Preview.Operations.Should().ContainSingle().Which.Skipped.Should().BeTrue();
            result.Preparation.Completion!.Succeeded.Should().BeTrue();
        }

        string configuration = File.ReadAllText(Path.Combine(project.Info.FullName, ".func", "config.json"));
        string package = File.ReadAllText(Path.Combine(project.Info.FullName, "package.json"));
        var rootItem = NodeComposedItem(project.Info.FullName, "HttpTrigger-" + language);
        rootItem.ItemCompatibility.Should().BeNull();
        rootItem.SelectedItemEffects.Should().BeNull();
        NewExecutionResult rootResult;
        using (var invocation = host.CreateInvocation())
        {
            var preview = await invocation.Composer.ExecuteItemAsync(rootItem with { DryRun = true }, CancellationToken.None);
            preview.Succeeded.Should().BeTrue(string.Join("; ", preview.Diagnostics));
            File.Exists(Path.Combine(project.Info.FullName, "httpTrigger" + extension)).Should().BeFalse();
            rootResult = await invocation.Composer.ExecuteItemAsync(rootItem, CancellationToken.None);
            rootResult.Invocation!.Changes.Should().BeEquivalentTo(preview.Invocation!.Changes);
        }

        rootResult.Succeeded.Should().BeTrue(string.Join("; ", rootResult.Diagnostics));
        rootResult.ItemName!.Name.Should().Be("httpTrigger");
        rootResult.ItemName.Origin.Should().Be("authoredDefault");
        rootResult.Context!.CommandDirectory.Info.FullName.Should().Be(project.Info.FullName);
        rootResult.Context.Project!.RootDirectory.Info.FullName.Should().Be(project.Info.FullName);
        File.ReadAllText(Path.Combine(project.Info.FullName, "httpTrigger" + extension)).Should().Contain("app.http('httpTrigger'");
        File.Exists(Path.Combine(project.Info.FullName, "src", "functions", "httpTrigger" + extension)).Should().BeFalse();
        var legacy = ReadNodeSource().Templates.Single(template => template.Id == "HttpTrigger-" + language);
        legacy.Actions!.Single(action => action.Type == "WriteToFile").FilePath
            .Should().Be("src/functions/$(FUNCTION_NAME_INPUT)" + extension);
        using (var metadata = JsonDocument.Parse(package))
        {
            metadata.RootElement.GetProperty("main").GetString().Should().Be(main);
        }

        string nested = Path.Combine(project.Info.FullName, "src", "functions");
        using (var invocation = host.CreateInvocation())
        {
            var result = await invocation.Composer.ExecuteItemAsync(NodeComposedItem(nested, "HTTP trigger"), CancellationToken.None);
            result.Succeeded.Should().BeTrue(string.Join("; ", result.Diagnostics));
            result.ItemName!.Name.Should().Be("httpTrigger");
            result.ItemName.Origin.Should().Be("authoredDefault");
            result.Context!.CommandDirectory.Info.FullName.Should().Be(nested);
        }
        File.ReadAllText(Path.Combine(nested, "httpTrigger" + extension)).Should().Contain("app.http('httpTrigger'");
        File.ReadAllText(Path.Combine(project.Info.FullName, ".func", "config.json")).Should().Be(configuration);
        File.ReadAllText(Path.Combine(project.Info.FullName, "package.json")).Should().Be(package);
    }
}
