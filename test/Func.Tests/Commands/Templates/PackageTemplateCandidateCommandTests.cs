// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Templates;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.Integration.Native;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Tests.Templates.Integration;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.Preparation;
using Azure.Functions.Cli.Tests.Templates.Selection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Commands.Templates;

public sealed class PackageTemplateCandidateCommandTests
{
    [Theory]
    [InlineData("init")]
    [InlineData("new")]
    public async Task Invoke_ExplicitCandidateGraph_TransportsTemplateOnlyTokensWithoutDynamicOptions(string command)
    {
        var fixture = Fixture();
        var type = command == "init" ? TemplateType.Project : TemplateType.Item;
        var candidate = fixture.Candidate(type, parameters: [SelectionFixtures.Parameter("Message", defaultValue: "default")]);
        fixture.SetCandidates(candidate);
        var interaction = new TestInteractionService();
        var root = new PackageTemplateCandidateGraph(new(fixture.Application, new(interaction)), new(fixture.Application, new(interaction))).Create();
        var tokens = Arguments(fixture, command, candidate.Descriptor.Metadata.Identity);

        var result = await root.Parse([.. tokens, "--", "--Message="]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        result.Should().Be(0);
        candidate.Arguments!.ValuesByCanonicalName["Message"].Should().BeEmpty();
        candidate.Arguments.ExplicitParameters.Should().Contain("Message");
        interaction.AllOutput.Should().Contain("SUCCESS:");
        fixture.Lease!.CleanupOutcome.Should().Be(Azure.Functions.Cli.Templates.PackageIntake.TemplatePackageCleanupOutcome.Released);
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--force")]
    [InlineData("--unknown")]
    public async Task Invoke_UnsupportedHostInput_FailsWithoutIntakeOrLegacyDispatch(params string[] extra)
    {
        var fixture = Fixture();
        fixture.SetCandidates(fixture.Candidate());
        var interaction = new TestInteractionService();
        var command = new PackageTemplateInitCommand(fixture.Application, new(interaction));

        var action = () => command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1), .. extra])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        await action.Should().ThrowAsync<GracefulException>();
        fixture.Lease.Should().BeNull();
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Invoke_TemplateTokensWithoutDelimiter_AreRejected()
    {
        var fixture = Fixture();
        var candidate = fixture.Candidate(parameters: [SelectionFixtures.Parameter()]);
        fixture.SetCandidates(candidate);
        var command = new PackageTemplateInitCommand(fixture.Application, new(new TestInteractionService()));

        var action = () => command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1), "--Message", "value"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        await action.Should().ThrowAsync<GracefulException>().WithMessage("*after '--'*");
        candidate.RenderCalls.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_DuplicateTemplateTokens_RemainDuplicateThroughCommandTransport()
    {
        var fixture = Fixture();
        var candidate = fixture.Candidate(parameters: [SelectionFixtures.Parameter()]);
        fixture.SetCandidates(candidate);
        var command = new PackageTemplateInitCommand(fixture.Application, new(new TestInteractionService()));

        var action = () => command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1), "--", "--Message=a", "--Message=b"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var failure = (await action.Should().ThrowAsync<GracefulException>()).Which;
        failure.InnerException.Should().BeOfType<TemplatePreparationException>().Which.Selection!.Status
            .Should().Be(Azure.Functions.Cli.Templates.Selection.TemplateSelectionStatus.InvalidArguments);
        candidate.RenderCalls.Should().Be(0);
    }

    [Fact]
    public async Task Invoke_DryRun_CapturesAndCleansWithoutPublication()
    {
        var fixture = Fixture();
        var candidate = fixture.Candidate();
        fixture.SetCandidates(candidate);
        var interaction = new TestInteractionService();
        var command = new PackageTemplateInitCommand(fixture.Application, new(interaction));

        var result = await command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1), "--dry-run", "--format", "json"])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        result.Should().Be(0);
        candidate.RenderCalls.Should().Be(1);
        fixture.Storage.Files.Should().BeEmpty();
        interaction.AllOutput.Should().Contain("\"preview\":true");
        interaction.AllOutput.Should().Contain("\"cleanupStatus\":2");
        interaction.AllOutput.Should().NotContain("captureId").And.NotContain("Fingerprint").And.NotContain("projectContext");
    }

    [Theory]
    [InlineData("package")]
    [InlineData("template")]
    public async Task Invoke_RepeatedHostOption_IsRejectedRatherThanLastWinning(string option)
    {
        var fixture = Fixture();
        fixture.SetCandidates(fixture.Candidate());
        var command = new PackageTemplateInitCommand(fixture.Application, new(new TestInteractionService()));
        var repeat = option == "package" ? fixture.Package.ArchivePath : "Fixture.Project";

        var parsed = command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1), "--" + option, repeat]);

        var result = await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        parsed.Errors.Should().NotBeEmpty();
        result.Should().NotBe(0);
        fixture.Lease.Should().BeNull();
    }

    [Fact]
    public void AddNativePackageTemplates_RegistersOnlyExplicitConcreteCandidateCommands_NotPublicDispatch()
    {
        var fixture = Fixture();
        var services = new ServiceCollection();
        services.AddSingleton<IFunctionsProjectResolver>(fixture.ProjectResolver);
        services.AddSingleton<IInteractionService>(new TestInteractionService());

        services.AddNativePackageTemplates(TemplatePackageTestData.Limits());

        services.Should().NotContain(service => service.ServiceType == typeof(FuncCliCommand));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var graph = provider.GetRequiredService<PackageTemplateCandidateGraph>().Create();
        graph.Subcommands.Select(command => command.Name).Should().Equal("init", "new");
        graph.Subcommands.SelectMany(command => command.Options).Should().NotContain(option => option.Name == "--force" || option.Name == "--output");
    }

    [Fact]
    public async Task Invoke_PreparationIoFailure_IsNotPresentedAsPublicationOrSuccess()
    {
        var fixture = Fixture();
        var failure = new IOException("Fixture source read failed.");
        fixture.Storage.FileSystem.OpenReadIfExists(fixture.Package.ArchivePath).Returns(_ => throw failure);
        var interaction = new TestInteractionService();
        var command = new PackageTemplateInitCommand(fixture.Application, new(interaction));

        var action = () => command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1)])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var surfaced = (await action.Should().ThrowAsync<GracefulException>()).Which;
        surfaced.Message.Should().StartWith("Package template preparation I/O failed:");
        surfaced.InnerException.Should().BeSameAs(failure);
        interaction.AllOutput.Should().NotContain("SUCCESS:");
    }

    [Fact]
    public async Task Invoke_PublicationConflict_PreservesTheExistingBytesAndReportsItsStage()
    {
        var fixture = Fixture();
        fixture.SetCandidates(fixture.Candidate());
        var occupied = PreparedOperationTestFileSystem.Destination("app.bin");
        fixture.Storage.SetFile(occupied, [42]);
        var interaction = new TestInteractionService();
        var command = new PackageTemplateInitCommand(fixture.Application, new(interaction));

        var action = () => command.Parse([.. Arguments(fixture, "init", "Fixture.Project").Skip(1)])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var surfaced = (await action.Should().ThrowAsync<GracefulException>()).Which;
        surfaced.Message.Should().StartWith("Create-only publication failed:").And.Contain("Status: Failed");
        surfaced.InnerException.Should().BeAssignableTo<IOException>();
        fixture.Storage.Files[occupied].Should().Equal([42]);
        interaction.AllOutput.Should().NotContain("SUCCESS:");
    }

    [Fact]
    public async Task Invoke_CandidateHelp_DoesNotAcquireContentOrDispatchLegacyGeneration()
    {
        var fixture = Fixture();
        var interaction = new TestInteractionService();
        var graph = new PackageTemplateCandidateGraph(new(fixture.Application, new(interaction)), new(fixture.Application, new(interaction))).Create();

        var result = await graph.Parse(["init", "--help"]).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        result.Should().Be(0);
        fixture.Lease.Should().BeNull();
        fixture.Storage.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("init", "same")]
    [InlineData("new", "same")]
    [InlineData("init", "different")]
    [InlineData("new", "")]
    public async Task Invoke_ReservedCommandAliases_UseParserFallbackWithoutChangingHostFormatOrUserProvenance(string command, string value)
    {
        var fixture = Fixture();
        var parameter = SelectionFixtures.Parameter("Format", defaultValue: "same") with { LongName = "format", ShortName = "f" };
        var candidate = fixture.Candidate(command == "init" ? TemplateType.Project : TemplateType.Item,
            parameters: [parameter, SelectionFixtures.Parameter("Message")]);
        fixture.SetCandidates(candidate);
        var interaction = new TestInteractionService();
        var init = new PackageTemplateInitCommand(fixture.Application, new(interaction));
        var @new = new PackageTemplateNewCommand(fixture.Application, new(interaction));
        PackageTemplateCommand invoked = command == "init" ? init : @new;
        invoked.FormatOption.Aliases.Add("-f");
        var graph = new PackageTemplateCandidateGraph(init, @new).Create();
        var parsed = graph.Parse([.. Arguments(fixture, command, candidate.Descriptor.Metadata.Identity),
            "-f", "json", "--", "--param:Format=" + value, "--Message=plain"]);

        var result = await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        result.Should().Be(0);
        parsed.GetValue(invoked.FormatOption).Should().Be("json");
        candidate.Arguments!.AliasesByCanonicalName["Format"].Should().Equal("--param:Format", "-p:f");
        candidate.Arguments.AliasesByCanonicalName["Message"].Should().Equal("--Message");
        candidate.Arguments.ValuesByCanonicalName["Format"].Should().Be(value);
        candidate.Arguments.ValuesByCanonicalName["Message"].Should().Be("plain");
        candidate.Arguments.ExplicitParameters.Should().Contain("Format");
        interaction.AllOutput.Should().Contain("JSON:");
        var nativeParameter = Substitute.For<ITemplateParameter>();
        nativeParameter.Name.Returns("Format");
        var definitions = new ParameterDefinitionSet([nativeParameter]);
        var native = Substitute.For<ITemplateInfo>();
        native.ParameterDefinitions.Returns(definitions);
        var input = NativePackageTemplateAdapter.CreateInput(native, candidate.Arguments);
        var nativeValue = input.Values.Should().ContainSingle().Which;
        nativeValue.Value.Should().Be(value);
        nativeValue.DataSource.Should().Be(DataSource.User);
    }

    [Fact]
    public async Task Invoke_ReservedInheritedAliases_KeepRootOptionAndTemplateValuesIndependent()
    {
        var fixture = Fixture();
        var candidate = fixture.Candidate(parameters:
            [SelectionFixtures.Parameter("Profile") with { LongName = "profile", ShortName = "p" }]);
        fixture.SetCandidates(candidate);
        var interaction = new TestInteractionService();
        var graph = new PackageTemplateCandidateGraph(new(fixture.Application, new(interaction)), new(fixture.Application, new(interaction))).Create();
        var profile = new Option<string>("--profile", "-p") { Recursive = true };
        graph.Options.Add(profile);
        var parsed = graph.Parse([.. Arguments(fixture, "init", candidate.Descriptor.Metadata.Identity),
            "-p", "host-profile", "--", "--param:Profile=template-profile"]);

        var result = await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        result.Should().Be(0);
        parsed.GetValue(profile).Should().Be("host-profile");
        candidate.Arguments!.AliasesByCanonicalName["Profile"].Should().Equal("--param:Profile", "-p:p");
        candidate.Arguments.ValuesByCanonicalName["Profile"].Should().Be("template-profile");
        candidate.Arguments.ExplicitParameters.Should().Contain("Profile");
    }

    [Theory]
    [InlineData("Help", "help", "h")]
    [InlineData("Output", "output", null)]
    public async Task Invoke_ReservedHelpAndRemovedOutputAliases_UseTheAcceptedLongFallback(
        string canonicalName, string longName, string? shortName)
    {
        var fixture = Fixture();
        var candidate = fixture.Candidate(parameters:
            [SelectionFixtures.Parameter(canonicalName) with { LongName = longName, ShortName = shortName }]);
        fixture.SetCandidates(candidate);
        var interaction = new TestInteractionService();
        var graph = new PackageTemplateCandidateGraph(new(fixture.Application, new(interaction)), new(fixture.Application, new(interaction))).Create();
        var parsed = graph.Parse([.. Arguments(fixture, "init", candidate.Descriptor.Metadata.Identity),
            "--", "--param:" + canonicalName + "=template-value"]);

        var result = await parsed.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        result.Should().Be(0);
        candidate.Arguments!.AliasesByCanonicalName[canonicalName].Should().Contain("--param:" + canonicalName).And.NotContain("--" + longName);
        if (shortName is not null)
        {
            candidate.Arguments.AliasesByCanonicalName[canonicalName].Should().NotContain("-" + shortName);
        }

        candidate.Arguments.ValuesByCanonicalName[canonicalName].Should().Be("template-value");
    }

    [Theory]
    [InlineData("plain", false)]
    [InlineData("json", false)]
    [InlineData("plain", true)]
    [InlineData("json", true)]
    public async Task Invoke_CancellationAfterFirstPublication_RendersRetainedOutcomeOnceAndPreservesCancellation(
        string format, bool cleanupFails)
    {
        var fixture = Fixture();
        var candidate = TwoFileItem(fixture);
        using var cancellation = new CancellationTokenSource();
        fixture.Storage.AfterPublishFile = _ => cancellation.Cancel();
        var cleanupFailure = new IOException("Retained native cleanup marker.");
        if (cleanupFails)
        {
            fixture.Session.CleanupFailure = cleanupFailure;
        }

        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(fixture.Application, new(interaction));

        var failure = await Record.ExceptionAsync(() => command
            .Parse([.. Arguments(fixture, "new", candidate.Descriptor.Metadata.Identity).Skip(1), "--format", format])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token));

        if (cleanupFails)
        {
            var combined = failure.Should().BeOfType<AggregateException>().Which;
            combined.InnerExceptions.Should().HaveCount(2);
            combined.InnerExceptions[0].Should().BeAssignableTo<OperationCanceledException>()
                .Which.CancellationToken.Should().Be(candidate.RenderToken);
            combined.InnerExceptions[1].Should().BeSameAs(cleanupFailure);
        }
        else
        {
            failure.Should().BeAssignableTo<OperationCanceledException>().Which.CancellationToken.Should().Be(candidate.RenderToken);
        }

        AssertFirstFileOnly(fixture, candidate);
        fixture.Storage.FileSystem.DidNotReceive().PublishFileNoReplace(
            Arg.Any<string>(), PreparedOperationTestFileSystem.Destination("second.txt"));
        AssertRetainedOutcome(interaction, format, PreparedCommitStatus.Canceled,
            cleanupFails ? PreparedCleanupStatus.Failed : PreparedCleanupStatus.Succeeded);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("json")]
    public async Task Invoke_CancellationAfterFirstPublication_CanceledCleanup_PreservesBothOutcomes(string format)
    {
        var fixture = Fixture();
        var candidate = TwoFileItem(fixture);
        using var cancellation = new CancellationTokenSource();
        using var cleanupCancellation = new CancellationTokenSource();
        cleanupCancellation.Cancel();
        fixture.Storage.AfterPublishFile = _ => cancellation.Cancel();
        fixture.Session.CleanupFailure = new OperationCanceledException("Native cleanup canceled.", cleanupCancellation.Token);
        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(fixture.Application, new(interaction));

        var action = () => command
            .Parse([.. Arguments(fixture, "new", candidate.Descriptor.Metadata.Identity).Skip(1), "--format", format])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        var combined = (await action.Should().ThrowAsync<AggregateException>()).Which;
        combined.InnerExceptions.Should().HaveCount(2);
        combined.InnerExceptions[0].Should().BeAssignableTo<OperationCanceledException>()
            .Which.CancellationToken.Should().Be(candidate.RenderToken);
        combined.InnerExceptions[1].Should().BeAssignableTo<OperationCanceledException>()
            .Which.CancellationToken.Should().Be(cleanupCancellation.Token);
        AssertFirstFileOnly(fixture, candidate);
        AssertRetainedOutcome(interaction, format, PreparedCommitStatus.Canceled, PreparedCleanupStatus.Failed);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("json")]
    public async Task Invoke_CommittedFailureThenCleanupCancellation_RendersFailureOnceAndPreservesNonzeroOutcome(string format)
    {
        var fixture = Fixture();
        var candidate = TwoFileItem(fixture);
        using var cancellation = new CancellationTokenSource();
        var publicationFailure = new IOException("Second publication refused.");
        fixture.Storage.BeforePublishFile = path =>
        {
            if (path == PreparedOperationTestFileSystem.Destination("second.txt"))
            {
                throw publicationFailure;
            }
        };
        fixture.Storage.DeleteFailure = _ =>
        {
            cancellation.Cancel();
            return null;
        };
        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(fixture.Application, new(interaction));

        int exit = await command
            .Parse([.. Arguments(fixture, "new", candidate.Descriptor.Metadata.Identity).Skip(1), "--format", format])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = true }, cancellation.Token);

        exit.Should().Be(1);
        cancellation.IsCancellationRequested.Should().BeTrue();
        AssertFirstFileOnly(fixture, candidate);
        AssertRetainedOutcome(interaction, format, PreparedCommitStatus.Failed, PreparedCleanupStatus.Succeeded);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("json")]
    public async Task Invoke_CancellationBeforeFirstPublication_DoesNotFabricateCommittedOutput(string format)
    {
        var fixture = Fixture();
        var candidate = TwoFileItem(fixture);
        using var cancellation = new CancellationTokenSource();
        fixture.Storage.BeforeWriteAsync = token =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(fixture.Application, new(interaction));

        var action = () => command
            .Parse([.. Arguments(fixture, "new", candidate.Descriptor.Metadata.Identity).Skip(1), "--format", format])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Storage.Files.Should().BeEmpty();
        fixture.Session.DisposeCalls.Should().Be(1);
        candidate.RenderCalls.Should().Be(1);
        interaction.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData("plain", false)]
    [InlineData("json", false)]
    [InlineData("plain", true)]
    [InlineData("json", true)]
    public async Task Invoke_SuccessfulPublication_RendersOnceEvenWhenCancellationArrivesAfterTheFinalEffect(
        string format, bool cancelAfterFinalEffect)
    {
        var fixture = Fixture();
        var candidate = TwoFileItem(fixture);
        using var cancellation = new CancellationTokenSource();
        if (cancelAfterFinalEffect)
        {
            fixture.Storage.AfterPublishFile = path =>
            {
                if (path == PreparedOperationTestFileSystem.Destination("second.txt"))
                {
                    cancellation.Cancel();
                }
            };
        }

        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(fixture.Application, new(interaction));

        int exit = await command
            .Parse([.. Arguments(fixture, "new", candidate.Descriptor.Metadata.Identity).Skip(1), "--format", format])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }, cancellation.Token);

        exit.Should().Be(0);
        fixture.Storage.Files.Should().HaveCount(2);
        candidate.RenderCalls.Should().Be(1);
        fixture.Session.DisposeCalls.Should().Be(1);
        interaction.Lines.Should().ContainSingle();
        if (format == "json")
        {
            using var json = JsonDocument.Parse(interaction.Lines[0]["JSON: ".Length..]);
            json.RootElement.GetProperty("status").GetInt32().Should().Be((int)PreparedCommitStatus.Succeeded);
            json.RootElement.GetProperty("completedEffects").GetArrayLength().Should().Be(2);
        }
        else
        {
            interaction.Lines[0].Should().StartWith("SUCCESS:");
        }
    }

    private static FixtureCandidate TwoFileItem(PackageTemplateFixture fixture)
    {
        var candidate = fixture.Candidate(TemplateType.Item);
        candidate.Capture = new(
        [
            new("first.txt", PreparedOutputKind.File, "first committed bytes"u8),
            new("second.txt", PreparedOutputKind.File, "second retained bytes"u8),
        ]);
        fixture.SetCandidates(candidate);
        return candidate;
    }

    private static void AssertFirstFileOnly(PackageTemplateFixture fixture, FixtureCandidate candidate)
    {
        fixture.Storage.Files.Keys.Should().Equal(PreparedOperationTestFileSystem.Destination("first.txt"));
        fixture.Storage.Files[PreparedOperationTestFileSystem.Destination("first.txt")].Should().Equal("first committed bytes"u8.ToArray());
        candidate.RenderCalls.Should().Be(1);
        fixture.Storage.FileSystem.Received(1).PublishFileNoReplace(Arg.Any<string>(), PreparedOperationTestFileSystem.Destination("first.txt"));
        fixture.Session.DisposeCalls.Should().Be(1);
        fixture.Lease!.CleanupOutcome.Should().Be(Azure.Functions.Cli.Templates.PackageIntake.TemplatePackageCleanupOutcome.Released);
    }

    private static void AssertRetainedOutcome(
        TestInteractionService interaction, string format, PreparedCommitStatus status, PreparedCleanupStatus cleanupStatus)
    {
        string committed = PreparedOperationTestFileSystem.Destination("first.txt");
        interaction.AllOutput.Should().NotContain("SUCCESS:");
        if (format == "json")
        {
            interaction.Lines.Should().ContainSingle();
            interaction.Lines[0].Should().StartWith("JSON: ");
            using var json = JsonDocument.Parse(interaction.Lines[0]["JSON: ".Length..]);
            json.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
            json.RootElement.GetProperty("cleanupStatus").GetInt32().Should().Be((int)cleanupStatus);
            json.RootElement.GetProperty("completedEffects").GetArrayLength().Should().Be(1);
            json.RootElement.GetProperty("completedEffects")[0].GetProperty("path").GetString().Should().Be(committed);
            json.RootElement.GetProperty("completedEffects")[0].GetProperty("kind").GetInt32().Should().Be((int)PreparedOutputKind.File);
            json.RootElement.GetProperty("preview").GetBoolean().Should().BeFalse();
        }
        else
        {
            interaction.Lines.Should().Equal(
                $"ERROR: Template 'Fixture.Item' did not complete successfully. Publication status: {status}; cleanup status: {cleanupStatus}.",
                $"File: {committed}");
        }
    }

    private static PackageTemplateFixture Fixture() => new()
    {
        ArchiveBytes = TemplatePackageTestData.Archive(
            manifest: TemplatePackageTestData.Manifest(types: ["Template", "FuncTemplate", "IntegrationFixture"])),
    };

    private static string[] Arguments(PackageTemplateFixture fixture, string command, string identity) =>
    [
        command, PreparedOperationTestFileSystem.Target,
        "--package", fixture.Package.ArchivePath,
        "--package-id", TemplatePackageTestData.PackageId,
        "--package-version", TemplatePackageTestData.PackageVersion,
        "--package-sha256", Convert.ToHexString(SHA256.HashData(fixture.ArchiveBytes)),
        "--template", identity,
    ];
}
