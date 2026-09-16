// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandTemplateOptionProviderTests
{
    private readonly INewCommandContextResolver _contextResolver = Substitute.For<INewCommandContextResolver>();
    private readonly INewCommandTemplateCatalog _templateCatalog = Substitute.For<INewCommandTemplateCatalog>();
    private readonly TemplateOptionHydrator _optionHydrator = new([]);

    [Fact]
    public async Task HydrateOptionsForTemplateWithIdsAsync_ResolutionFailure_ReturnsNullWithoutListingTemplates()
    {
        NewCommandTemplateOptionProvider provider = CreateProvider();
        NewInvocation invocation = CreateInvocation();
        _contextResolver.ResolveAsync(invocation, Arg.Any<CancellationToken>())
            .Returns(NewCommandResolutionResult.Fail(
                new NewCommandResolutionFailure(NewCommandResolutionFailureKind.ProjectRequired)));

        IReadOnlyList<HydratedTemplateOption>? result = await provider.HydrateOptionsForTemplateWithIdsAsync(
            invocation,
            "HttpTrigger",
            CancellationToken.None);

        result.Should().BeNull();
        await _templateCatalog.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
    }

    [Fact]
    public async Task HydrateOptionsForTemplateWithIdsAsync_MatchingTemplate_ReturnsPromptIdsAndOptions()
    {
        NewCommandTemplateOptionProvider provider = CreateProvider();
        NewInvocation invocation = CreateInvocation();
        NewCommandResolvedContext context = CreateContext();
        FunctionTemplateInfo template = CreateTemplate();
        _contextResolver.ResolveAsync(invocation, Arg.Any<CancellationToken>())
            .Returns(NewCommandResolutionResult.Succeed(context));
        _templateCatalog.ListAsync(context, Arg.Any<CancellationToken>()).Returns([template]);

        IReadOnlyList<HydratedTemplateOption>? result = await provider.HydrateOptionsForTemplateWithIdsAsync(
            invocation,
            "httptrigger",
            CancellationToken.None);

        result.Should().ContainSingle();
        result![0].PromptId.Should().Be("authLevel");
        result[0].Option.Name.Should().Be("--auth-level");
    }

    [Fact]
    public async Task HydrateOptionsForTemplateAsync_MatchingTemplate_ReturnsOptionsOnly()
    {
        NewCommandTemplateOptionProvider provider = CreateProvider();
        NewInvocation invocation = CreateInvocation();
        NewCommandResolvedContext context = CreateContext();
        FunctionTemplateInfo template = CreateTemplate();
        _contextResolver.ResolveAsync(invocation, Arg.Any<CancellationToken>())
            .Returns(NewCommandResolutionResult.Succeed(context));
        _templateCatalog.ListAsync(context, Arg.Any<CancellationToken>()).Returns([template]);

        IReadOnlyList<Option>? result = await provider.HydrateOptionsForTemplateAsync(
            invocation,
            template.Id,
            CancellationToken.None);

        result.Should().ContainSingle().Which.Name.Should().Be("--auth-level");
    }

    private NewCommandTemplateOptionProvider CreateProvider()
    {
        return new NewCommandTemplateOptionProvider(_contextResolver, _templateCatalog, _optionHydrator);
    }

    private static NewInvocation CreateInvocation()
    {
        WorkingDirectory workingDirectory = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        return new NewInvocation(
            workingDirectory,
            RequestedTemplate: "HttpTrigger",
            RequestedFunctionName: null,
            Force: false,
            NonInteractive: true);
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

    private static FunctionTemplateInfo CreateTemplate()
    {
        TemplateUserPrompt prompt = new(
            "authLevel",
            "Authorization level",
            "string",
            DefaultValue: "function",
            Choices: ["anonymous", "function", "admin"],
            IsRequired: false,
            ValidatorRegex: null,
            ShortAlias: null,
            LongAlias: null);
        return new FunctionTemplateInfo(
            "HttpTrigger",
            "node",
            EngineIds.V2,
            "HTTP trigger",
            Description: null,
            DefaultFunctionName: null,
            Languages: ["javascript"],
            new TemplateMetadata([prompt], RequiresExtensionBundle: false, MinBundleVersion: null));
    }
}