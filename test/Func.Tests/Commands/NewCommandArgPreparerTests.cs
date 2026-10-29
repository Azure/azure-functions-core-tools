// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.V2;
using Azure.Functions.Cli.Tests.Templates.Integration;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands;

[CollectionDefinition(nameof(NewCommandArgPreparerTests), DisableParallelization = true)]
public sealed class NewCommandArgPreparerCollection;

[Collection(nameof(NewCommandArgPreparerTests))]
public sealed class NewCommandArgPreparerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicPreParser_AuthoredExistingOption_UsesCurrentOrExplicitProject(bool qualification, bool explicitPath)
    {
        using var fixture = new PublicPackageTemplatesFixture(enabled: qualification);
        fixture.WriteProject();
        await ConfigureExistingQueueTemplateAsync(fixture, CancellationToken.None);
        using var directory = new CurrentDirectoryScope(explicitPath ? fixture.Root : fixture.Target);
        string[] path = explicitPath ? [fixture.Target] : [];

        int result = await fixture.InvokeAsync(
            ["new", .. path, "-t", "QueueTrigger-JavaScript", "-n", "QueueProbe",
                "--queue-trigger-name", "reviewed-queue", "--queue-trigger-connection", "AzureWebJobsStorage", "--non-interactive"]);

        result.Should().Be(0);
        fixture.New.Options.Should().Contain(option => option.Name == "--queue-trigger-name");
        await fixture.LegacyApplicator.Received(1).ApplyAsync(
            Arg.Is<NewInvocation>(invocation =>
                invocation.WorkingDirectory.Info.FullName == fixture.Target
                && invocation.WorkingDirectory.WasExplicit == explicitPath
                && invocation.UserOptionValues!["queueTrigger-name"] == "reviewed-queue"
                && invocation.UserOptionValues["queueTrigger-connection"] == "AzureWebJobsStorage"),
            Arg.Any<NewCommandResolvedContext>(), Arg.Any<FunctionTemplateInfo>(), Arg.Any<CancellationToken>());
        fixture.ResourceReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicPreParser_PackageHttp_StillSelectsOnlyThePackageRoute(bool explicitPath)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        using var directory = new CurrentDirectoryScope(explicitPath ? fixture.Root : fixture.Target);
        string[] path = explicitPath ? [fixture.Target] : [];

        int result = await fixture.InvokeAsync(
            ["new", .. path, "-t", PublicPackageTemplatesFixture.PublicHttp, "-n", "NativeProbe", "--non-interactive"]);

        result.Should().Be(0);
        File.Exists(Path.Combine(fixture.Target, "src", "functions", "NativeProbe.js")).Should().BeTrue();
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
        fixture.ResourceReads.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicPreParser_InvalidExplicitProject_DoesNotHydrateOrRunAgainstCurrentProject(bool qualification)
    {
        using var fixture = new PublicPackageTemplatesFixture(enabled: qualification);
        fixture.WriteProject();
        await ConfigureExistingQueueTemplateAsync(fixture, CancellationToken.None);
        using var directory = new CurrentDirectoryScope(fixture.Target);
        string missing = Path.Combine(fixture.Root, "not-a-project");

        int result = await fixture.InvokeAsync(
            "new", missing, "-t", "QueueTrigger-JavaScript", "--queue-trigger-name", "reviewed-queue");

        result.Should().NotBe(0);
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
        fixture.ResourceReads.Should().BeEmpty();
        Directory.Exists(missing).Should().BeFalse();
        fixture.LegacyContext.ReceivedCalls().Should().OnlyContain(call =>
            ((NewInvocation)call.GetArguments()[0]!).WorkingDirectory.Info.FullName == missing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicPreParser_MalformedExistingOption_RemainsAnErrorWithoutGeneration(bool qualification)
    {
        using var fixture = new PublicPackageTemplatesFixture(enabled: qualification);
        fixture.WriteProject();
        await ConfigureExistingQueueTemplateAsync(fixture, CancellationToken.None);
        using var directory = new CurrentDirectoryScope(fixture.Target);

        int result = await fixture.InvokeAsync(
            "new", "-t", "QueueTrigger-JavaScript", "--queue-trigger-name", "reviewed-queue", "--queue-trigger-connection");

        result.Should().NotBe(0);
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
        fixture.ResourceReads.Should().BeEmpty();
        Directory.Exists(Path.Combine(fixture.Target, "src")).Should().BeFalse();
    }

    private static async Task ConfigureExistingQueueTemplateAsync(PublicPackageTemplatesFixture fixture, CancellationToken cancellationToken)
    {
        string install = Path.Combine(fixture.Root, "existing-templates");
        await CopyResourceAsync("templates.json", Path.Combine(install, "tools", "any", "content", "v2", "templates", "templates.json"), cancellationToken);
        await CopyResourceAsync("userPrompts.json", Path.Combine(install, "tools", "any", "content", "v2", "bindings", "userPrompts.json"), cancellationToken);
        var provider = new V2EngineProvider(Substitute.For<IInstalledTemplatesWorkloads>());
        var workingDirectory = WorkingDirectory.FromExplicit(fixture.Target);
        var templates = await provider.ListTemplatesAsync(new(workingDirectory, "node", "javascript", install), cancellationToken);
        FunctionTemplateInfo template = templates.Single(item => item.Id == "QueueTrigger-JavaScript");
        template.Metadata.UserPrompts.Should().Contain(prompt => prompt.Id == "queueTrigger-name");
        template.Metadata.UserPrompts.Should().Contain(prompt => prompt.Id == "queueTrigger-connection");
        fixture.LegacyCatalog.ListAsync(Arg.Any<NewCommandResolvedContext>(), Arg.Any<CancellationToken>())
            .Returns([template]);
        fixture.LegacyContext.ResolveAsync(Arg.Any<NewInvocation>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var invocation = call.ArgAt<NewInvocation>(0);
                return invocation.WorkingDirectory.Info.FullName == fixture.Target
                    ? NewCommandResolutionResult.Succeed(new(invocation.WorkingDirectory, "node", "javascript",
                        new("node", "1.0.0", install), null, BundleChannel.Stable, false))
                    : NewCommandResolutionResult.Fail(new(NewCommandResolutionFailureKind.ProjectRequired));
            });
    }

    private static async Task CopyResourceAsync(string resourceName, string destination, CancellationToken cancellationToken)
    {
        await using Stream source = typeof(NewCommandArgPreparerTests).Assembly.GetManifestResourceStream(
            "Azure.Functions.Cli.Tests.PreParser.LegacyNode." + resourceName)
            ?? throw new InvalidOperationException($"Missing existing template test resource '{resourceName}'.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using FileStream output = File.Create(destination);
        await source.CopyToAsync(output, cancellationToken);
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _original = Environment.CurrentDirectory;

        public CurrentDirectoryScope(string path)
        {
            Environment.CurrentDirectory = path;
        }

        public void Dispose() => Environment.CurrentDirectory = _original;
    }
}
