// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates;

public class NewCommandTemplateApplicatorTests
{
    private readonly ITemplateEngineProviderRegistry _registry = Substitute.For<ITemplateEngineProviderRegistry>();
    private readonly TemplateOptionHydrator _optionHydrator = new([]);

    [Fact]
    public async Task ApplyAsync_MissingEngine_ReturnsProviderFailure()
    {
        NewCommandTemplateApplicator applicator = new(_registry, _optionHydrator);
        FunctionTemplateInfo template = CreateTemplate();
        _registry.TryGet(template.EngineId).Returns((ITemplateEngineProvider?)null);

        TemplateApplicationResult result = await applicator.ApplyAsync(
            CreateInvocation(),
            CreateContext(),
            template,
            CancellationToken.None);

        TemplateApplicationResult.Failed failed = result.Should().BeOfType<TemplateApplicationResult.Failed>().Subject;
        failed.Failure.Should().BeOfType<TemplateApplicationFailure.ProviderError>()
            .Which.Message.Should().Contain("No engine registered for EngineId 'v2'");
    }

    [Fact]
    public async Task ApplyAsync_RegisteredEngine_PassesResolvedContextAndReturnsProviderResult()
    {
        NewCommandTemplateApplicator applicator = new(_registry, _optionHydrator);
        FunctionTemplateInfo template = CreateTemplate();
        NewInvocation invocation = CreateInvocation();
        NewCommandResolvedContext context = CreateContext();
        ITemplateEngineProvider provider = Substitute.For<ITemplateEngineProvider>();
        _registry.TryGet(template.EngineId).Returns(provider);
        TemplateApplicationResult expected = new TemplateApplicationResult.Created(["Function.js"]);
        NewContext? receivedContext = null;
        provider.ApplyAsync(
                Arg.Do<NewContext>(value => receivedContext = value),
                Arg.Any<ParseResult>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        TemplateApplicationResult result = await applicator.ApplyAsync(
            invocation,
            context,
            template,
            CancellationToken.None);

        result.Should().BeSameAs(expected);
        receivedContext.Should().NotBeNull();
        receivedContext!.WorkingDirectory.Should().Be(invocation.WorkingDirectory);
        receivedContext.FunctionName.Should().Be(invocation.RequestedFunctionName);
        receivedContext.Language.Should().Be(context.Language);
        receivedContext.Force.Should().BeTrue();
        receivedContext.InstallDirectory.Should().Be(context.Workload.InstallDirectory);
        receivedContext.UserOptionValues.Should().BeSameAs(invocation.UserOptionValues);
        await provider.Received(1).ApplyAsync(
            Arg.Any<NewContext>(),
            Arg.Any<ParseResult>(),
            CancellationToken.None);
    }

    private static NewInvocation CreateInvocation()
    {
        WorkingDirectory workingDirectory = new(new DirectoryInfo(Path.GetTempPath()), WasExplicit: false);
        Dictionary<string, string?> optionValues = new(StringComparer.OrdinalIgnoreCase)
        {
            ["authLevel"] = "anonymous",
        };
        return new NewInvocation(
            workingDirectory,
            RequestedTemplate: "HttpTrigger",
            RequestedFunctionName: "MyFunction",
            Force: true,
            NonInteractive: true,
            UserOptionValues: optionValues);
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
        return new FunctionTemplateInfo(
            "HttpTrigger",
            "node",
            EngineIds.V2,
            "HTTP trigger",
            Description: null,
            DefaultFunctionName: "HttpTrigger",
            Languages: ["javascript"],
            new TemplateMetadata([], RequiresExtensionBundle: false, MinBundleVersion: null));
    }
}