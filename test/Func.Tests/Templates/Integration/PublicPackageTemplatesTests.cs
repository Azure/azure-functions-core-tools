// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.PackageIntake;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

public sealed class PublicPackageTemplatesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Init_PublicParser_UsesTheBoundDefaultAndPreservesUnrelatedFiles(bool noBundles)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.Write("unrelated.txt", "keep");

        string[] bundle = noBundles ? ["--no-bundles"] : [];
        int result = await fixture.InvokeAsync(["init", fixture.Target, "--stack", "node", "--language", "JavaScript", "--skip-npm-install", .. bundle]);

        result.Should().Be(0);
        File.ReadAllText(Path.Combine(fixture.Target, "package.json")).Should().Contain("qualification-node");
        File.ReadAllText(Path.Combine(fixture.Target, ".func", "config.json")).Should().Contain("JavaScript");
        File.ReadAllText(Path.Combine(fixture.Target, "unrelated.txt")).Should().Be("keep");
        fixture.ResourceReads.Should().Equal(PublicPackageTemplatesFixture.ResourceName);
        fixture.Readers.Should().OnlyContain(reader => reader.CloseCount == 1);
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("--name", "custom")]
    [InlineData("--bundles-channel", "Stable")]
    [InlineData("--bundles-channel", "Preview")]
    public async Task Init_UnsupportedQualificationMode_RejectsBeforeCreatingTheTarget(params string[] mode)
    {
        using var fixture = new PublicPackageTemplatesFixture();

        var action = () => fixture.InvokeAsync(["init", fixture.Target, "--stack", "node", "--language", "JavaScript", .. mode]);

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*qualification*");
        Directory.Exists(fixture.Target).Should().BeFalse();
        fixture.ResourceReads.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Init_ExistingAdoptionOrHealing_PreservesConfigurationWithoutAcquiringPackage(bool heal)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.Write("package.json", "{}");
        fixture.Write("host.json", """{"version":"2.0"}""");
        fixture.Write("unrelated.txt", "keep");
        if (heal)
        {
            fixture.Write(".func/config.json", """{"stack":{"runtime":"node"},"custom":{"preserve":42},"profiles":{"existing":{}}}""");
        }

        int result = await fixture.InvokeAsync("init", fixture.Target, "--stack", "node", "--language", "JavaScript");

        result.Should().Be(0);
        string config = File.ReadAllText(Path.Combine(fixture.Target, ".func", "config.json"));
        if (heal)
        {
            config.Should().Contain("42").And.Contain("existing");
        }

        File.ReadAllText(Path.Combine(fixture.Target, "package.json")).Should().Be("{}");
        File.ReadAllText(Path.Combine(fixture.Target, "unrelated.txt")).Should().Be("keep");
        fixture.ResourceReads.Should().BeEmpty();
        fixture.Resolutions.Should().OnlyContain(context => !context.InspectCapabilities);
    }

    [Theory]
    [InlineData(null, "httpTrigger")]
    [InlineData("Chosen", "Chosen")]
    [InlineData("httpTrigger", "httpTrigger")]
    public async Task New_PublicAliases_PreserveExplicitAndNativeDefaultNaming(string? name, string expected)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        var before = fixture.Snapshot();
        string[] names = name is null ? [] : ["-n", name];

        int result = await fixture.InvokeAsync(["new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp, .. names]);

        result.Should().Be(0);
        string generated = Path.Combine(fixture.Target, "src", "functions", expected + ".js");
        File.ReadAllText(generated).Should().Contain($"app.http('{expected}'");
        foreach (var entry in before)
        {
            File.ReadAllText(Path.Combine(fixture.Target, entry.Key)).Should().Be(entry.Value);
        }

        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
        fixture.Resolutions.Should().ContainSingle().Which.InspectCapabilities.Should().BeTrue();
        fixture.Readers.Should().OnlyContain(reader => reader.CloseCount == 1);
    }

    [Theory]
    [InlineData("JavaScript", "QueueTrigger-JavaScript", true)]
    [InlineData("TypeScript", "HttpTrigger-JavaScript", true)]
    public async Task New_ExplicitSplit_ChoosesExistingRouteUpfrontWithoutPackageAcquisition(string language, string template, bool force)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject(language);
        string[] flags = force ? ["--force"] : [];

        int result = await fixture.InvokeAsync(["new", fixture.Target, "-t", template, "-n", "Existing", .. flags]);

        result.Should().Be(0);
        fixture.ResourceReads.Should().BeEmpty();
        await fixture.LegacyApplicator.Received(1).ApplyAsync(
            Arg.Is<NewInvocation>(invocation => invocation.Force == force && invocation.RequestedFunctionName == "Existing"),
            Arg.Any<NewCommandResolvedContext>(), Arg.Any<FunctionTemplateInfo>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task New_ForceOnPackageRoute_RejectsWithoutMutationOrFallback()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        var before = fixture.Snapshot();

        var action = () => fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp, "--force");

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*create-only*--force*");
        fixture.Snapshot().Should().BeEquivalentTo(before);
        fixture.ResourceReads.Should().BeEmpty();
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("package.json", "{ broken")]
    [InlineData("host.json", "{ broken")]
    [InlineData("package.json", """{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js","type":"module"}""")]
    public async Task New_MalformedOrIncompatibleProject_FailsBeforePublication(string path, string content)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        fixture.Write(path, content);
        var before = fixture.Snapshot();

        var action = () => fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp);

        await action.Should().ThrowAsync<GracefulException>();
        fixture.Snapshot().Should().BeEquivalentTo(before);
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task New_InspectionIoFailure_IsNotAnEmptyReportOrLegacyFallback()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        fixture.InspectionFailure = new IOException("Cannot inspect package.json.");
        var before = fixture.Snapshot();

        var action = () => fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp);

        var failure = (await action.Should().ThrowAsync<GracefulException>()).Which;
        failure.InnerException.Should().BeSameAs(fixture.InspectionFailure);
        fixture.Snapshot().Should().BeEquivalentTo(before);
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task New_Collision_PreservesExistingFileAndConfiguration()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        fixture.Write("src/functions/httpTrigger.js", "unowned existing file");
        var before = fixture.Snapshot();

        var action = () => fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp);

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*Create-only*");
        fixture.Snapshot().Should().BeEquivalentTo(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Help_BasicDoesNotAcquireAndSelectedHelpUsesNativeNameMetadata(bool selected)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        string[] selection = selected ? ["-t", PublicPackageTemplatesFixture.PublicHttp] : [];

        int result = await fixture.InvokeAsync(["new", fixture.Target, .. selection, "--help"]);

        result.Should().Be(0);
        if (selected)
        {
            fixture.New.NameOption.Description.Should().Contain("httpTrigger").And.Contain("qualification");
            fixture.ResourceReads.Should().NotBeEmpty();
        }
        else
        {
            fixture.ResourceReads.Should().BeEmpty();
        }

        fixture.New.Options.Should().NotContain(option => option.Name.StartsWith("--package", StringComparison.Ordinal));
        fixture.Init.Options.Should().NotContain(option => option.Name.StartsWith("--package", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Listing_PreservesBroaderVisibleCatalogAndReplacesOnlyTheQualifiedHttpEntry()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();

        int result = await fixture.InvokeAsync("new", fixture.Target, "--list", "--output", "json");

        result.Should().Be(0);
        fixture.Interaction.AllOutput.Should().Contain("QueueTrigger-JavaScript").And.Contain("httpTrigger").And.Contain("native-qualification");
        fixture.Interaction.AllOutput.Split("\"id\": \"HttpTrigger-JavaScript\"", StringSplitOptions.None).Should().HaveCount(2);
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_AuthoredProjectWithoutBundle_StillListsItsCompatibleNativeHttp()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        fixture.Write("host.json", """{"version":"2.0"}""");
        fixture.LegacyContext.ResolveAsync(Arg.Any<NewInvocation>(), Arg.Any<CancellationToken>())
            .Returns(NewCommandResolutionResult.Fail(new(NewCommandResolutionFailureKind.HostJsonBundleMissing)));

        int result = await fixture.InvokeAsync("new", fixture.Target, "--list", "--output", "json");

        result.Should().Be(0);
        fixture.Interaction.AllOutput.Should().Contain("HttpTrigger-JavaScript").And.NotContain("QueueTrigger-JavaScript");
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_HiddenOrdinaryHttp_DoesNotReappearFromLegacyButRemainsExplicitlyInvocable()
    {
        using var fixture = new PublicPackageTemplatesFixture(packageBytes: PublicPackageTemplatesFixture.Archive(hidden: true));
        fixture.WriteProject();

        int listed = await fixture.InvokeAsync("new", fixture.Target, "--list", "--output", "json");

        listed.Should().Be(0);
        fixture.Interaction.AllOutput.Should().Contain("QueueTrigger-JavaScript").And.NotContain("\"id\": \"HttpTrigger-JavaScript\"");
        int created = await fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.ItemIdentity);
        created.Should().Be(0);
        File.Exists(Path.Combine(fixture.Target, "src", "functions", "httpTrigger.js")).Should().BeTrue();
    }

    [Fact]
    public async Task Listing_IncompatibleHttp_IsNotAdvertisedOrReplacedByLegacyHttp()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        fixture.Write("package.json", """{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js","type":"module"}""");

        int result = await fixture.InvokeAsync("new", fixture.Target, "--list", "--output", "json");

        result.Should().Be(0);
        fixture.Interaction.AllOutput.Should().Contain("QueueTrigger-JavaScript").And.NotContain("\"id\": \"HttpTrigger-JavaScript\"");
        fixture.Resolutions.Should().ContainSingle().Which.InspectCapabilities.Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"type":"func-project-capabilities","args":[]}""")]
    [InlineData("""{"type":"unsupported","args":["a"]}""")]
    public async Task New_InvalidRequiredNativeDeclaration_IsAContentFailureBeforeGeneration(string declaration)
    {
        using var fixture = new PublicPackageTemplatesFixture(packageBytes: PublicPackageTemplatesFixture.Archive(requirement: declaration));
        fixture.WriteProject();
        var before = fixture.Snapshot();

        var action = () => fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp);

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*metadata*");
        fixture.Snapshot().Should().BeEquivalentTo(before);
        fixture.LegacyApplicator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task New_JsonCompletion_IsReportedOnceAfterCleanupWithActualEffects()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();

        int result = await fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp, "--output", "json");

        result.Should().Be(0);
        fixture.Interaction.AllOutput.Split("\"command\":\"new\"", StringSplitOptions.None).Should().HaveCount(2);
        fixture.Interaction.AllOutput.Should().Contain("src").And.Contain("httpTrigger.js").And.Contain("\"cleanupStatus\":2");
        fixture.Interaction.AllOutput.Should().NotContain("SUCCESS:");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_UnsupportedOutputMode_IsExplicitAndDoesNotMutateTheProject(bool list)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        var before = fixture.Snapshot();
        string[] selection = list ? ["--list"] : ["-t", PublicPackageTemplatesFixture.PublicHttp];

        var action = () => fixture.InvokeAsync(["new", fixture.Target, .. selection, "--output", "yaml"]);

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*--output plain*--output json*");
        fixture.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task New_RawRepeatedHostAliases_AreRejectedBeforeGeneration()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        var before = fixture.Snapshot();

        int result = await fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp, "-n", "First", "--name", "Second");

        result.Should().NotBe(0);
        fixture.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task New_UnsupportedHttpTokens_AreRejectedBySharedParsingRatherThanBecomingParameters()
    {
        using var fixture = new PublicPackageTemplatesFixture();
        fixture.WriteProject();
        var before = fixture.Snapshot();

        var action = () => fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp,
            "--", "--auth-level", "anonymous", "--auth-level", "anonymous");

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*SelectionRejected*");
        fixture.Snapshot().Should().BeEquivalentTo(before);
        fixture.New.Options.Should().NotContain(option => option.Name == "--auth-level" || option.Name == "--methods" || option.Name == "--route");
    }

    [Theory]
    [InlineData("resource")]
    [InlineData("wrong-resource")]
    [InlineData("tampered")]
    [InlineData("identity")]
    [InlineData("version")]
    [InlineData("default")]
    public async Task Binding_InvalidResourceIdentityIntegrityOrDefault_FailsClosedBeforeCreatingTarget(string defect)
    {
        using var fixture = new PublicPackageTemplatesFixture(
            defaultProject: defect == "default" ? "Wrong.Project" : null,
            expectedId: defect == "identity" ? "Wrong.Package" : null,
            expectedVersion: defect == "version" ? "99.0.0" : null,
            resourceName: defect == "wrong-resource" ? "Wrong.Resource" : null);
        if (defect == "resource")
        {
            fixture.ResourceFailure = new FileNotFoundException("Missing bound resource.");
        }
        else if (defect == "tampered")
        {
            fixture.Bytes = [.. fixture.Bytes, 0];
        }

        var action = () => fixture.InvokeAsync("init", fixture.Target, "--stack", "node", "--language", "JavaScript", "--skip-npm-install");

        await action.Should().ThrowAsync<GracefulException>();
        Directory.Exists(fixture.Target).Should().BeFalse();
        fixture.Readers.Should().OnlyContain(reader => reader.CloseCount == 1);
    }

    [Fact]
    public async Task Binding_Disabled_NormalPublicRouteDoesNotAcquireQualificationContent()
    {
        using var fixture = new PublicPackageTemplatesFixture(enabled: false);
        fixture.WriteProject();

        int result = await fixture.InvokeAsync("new", fixture.Target, "-t", PublicPackageTemplatesFixture.PublicHttp, "--force");

        result.Should().Be(0);
        fixture.ResourceReads.Should().BeEmpty();
        fixture.Packages.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void NormalComposition_DoesNotEnableOrEmbedTheQualificationBinding()
    {
        var provider = TestParser.BuildServiceProviderWith(new TestInteractionService());
        try
        {
            provider.GetRequiredService<IPublicPackageTemplates>().IsEnabled.Should().BeFalse();
            typeof(PublicPackageTemplates).Assembly.GetManifestResourceNames()
                .Should().NotContain("Azure.Functions.Cli.Templates.Qualification.Node.Basic.nupkg");
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("init")]
    [InlineData("new")]
    [InlineData("metadata")]
    public async Task PackageOperations_PreCanceled_DoNotAcquireOrPublish(string operation)
    {
        using var fixture = new PublicPackageTemplatesFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var directory = WorkingDirectory.FromExplicit(fixture.Target);
        Func<Task> action = operation switch
        {
            "init" => () => fixture.Packages.InitializeAsync(directory, cancellation.Token),
            "new" => () => fixture.Packages.CreateAsync(
                new(directory, PublicPackageTemplatesFixture.PublicHttp, null, false, true), cancellation.Token),
            _ => () => fixture.Packages.ReadItemAsync(directory, cancellation.Token),
        };

        await action.Should().ThrowAsync<OperationCanceledException>();

        fixture.ResourceReads.Should().BeEmpty();
        Directory.Exists(fixture.Target).Should().BeFalse();
    }

    [Fact]
    public void Binding_RequiresIndependentDigestAndResourceName()
    {
        var missingDigest = () => new PublicTemplatePackageBinding(new("Fixture.Package", "1.0.0", "FuncTemplate"),
            "resource", "project", "item", "HttpTrigger");
        var missingResource = () => new PublicTemplatePackageBinding(new("Fixture.Package", "1.0.0", "FuncTemplate", new string('A', 64)),
            "", "project", "item", "HttpTrigger");

        missingDigest.Should().Throw<ArgumentException>();
        missingResource.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("item")]
    public async Task Binding_WrongOrAmbiguousProjectDefault_IsRejected(string defect)
    {
        var fixture = new PackageTemplateFixture();
        var project = fixture.Candidate(TemplateType.Project, identity: "Bound.Project");
        FixtureCandidate[] candidates = defect switch
        {
            "missing" => [fixture.Candidate(TemplateType.Project, identity: "Other.Project")],
            "duplicate" => [project, fixture.Candidate(TemplateType.Project, identity: "Bound.Project")],
            _ => [fixture.Candidate(TemplateType.Item, identity: "Bound.Project")],
        };
        await using var session = new FixtureSession(candidates);

        var action = () => PackageTemplateApplication.ValidateProjectDefault(session, "Bound.Project");

        action.Should().Throw<InvalidTemplateMetadataException>().WithMessage("*exactly one ordinary project default*");
    }
}
