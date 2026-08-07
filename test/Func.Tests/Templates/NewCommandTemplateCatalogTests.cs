// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandTemplateCatalogTests
{
    [Fact]
    public async Task ListAsync_MultipleProviders_AggregatesTemplatesInRegistrationOrder()
    {
        NewCommandResolvedContext context = CreateContext();
        FunctionTemplateInfo firstTemplate = CreateTemplate("First", EngineIds.V2);
        FunctionTemplateInfo secondTemplate = CreateTemplate("Second", EngineIds.DotNet);
        ITemplateEngineProvider firstProvider = Substitute.For<ITemplateEngineProvider>();
        ITemplateEngineProvider secondProvider = Substitute.For<ITemplateEngineProvider>();
        firstProvider.ListTemplatesAsync(Arg.Any<TemplateListContext>(), Arg.Any<CancellationToken>())
            .Returns([firstTemplate]);
        secondProvider.ListTemplatesAsync(Arg.Any<TemplateListContext>(), Arg.Any<CancellationToken>())
            .Returns([secondTemplate]);
        ITemplateEngineProviderRegistry registry = Substitute.For<ITemplateEngineProviderRegistry>();
        registry.Providers.Returns([firstProvider, secondProvider]);
        NewCommandTemplateCatalog catalog = new(registry);

        IReadOnlyList<FunctionTemplateInfo> result = await catalog.ListAsync(context, CancellationToken.None);

        result.Should().ContainInOrder(firstTemplate, secondTemplate);
        await firstProvider.Received(1).ListTemplatesAsync(
            Arg.Is<TemplateListContext>(listContext =>
                listContext.WorkingDirectory == context.WorkingDirectory
                && listContext.Stack == context.Stack
                && listContext.Language == context.Language
                && listContext.InstallDirectory == context.Workload.InstallDirectory),
            CancellationToken.None);
    }

    private static NewCommandResolvedContext CreateContext()
    {
        WorkingDirectory workingDirectory = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        return new NewCommandResolvedContext(
            workingDirectory,
            "node",
            "javascript",
            new InstalledTemplatesWorkload("node", "1.0.0", "install"),
            BundleId: null,
            BundleChannel.Unknown,
            UsedStableFallback: false);
    }

    private static FunctionTemplateInfo CreateTemplate(string id, string engineId)
    {
        return new FunctionTemplateInfo(
            id,
            "node",
            engineId,
            id,
            Description: null,
            DefaultFunctionName: null,
            Languages: ["javascript"],
            new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
    }
}