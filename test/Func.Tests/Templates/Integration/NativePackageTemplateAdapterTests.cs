// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Azure.Functions.Cli.Commands.Templates;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.Integration.Native;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.Selection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Parameters;
using Microsoft.TemplateEngine.Edge.Template;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

public sealed class NativePackageTemplateAdapterTests : IDisposable
{
    private readonly IFileSystem _files = new PhysicalFileSystem();
    private readonly TemplatePackageIntakeLimits _limits = TemplatePackageTestData.Limits();
    private readonly string _root;

    public NativePackageTemplateAdapterTests()
    {
        _root = _files.CreateTempDirectory().Path;
    }

    public void Dispose() => _files.DeleteDirectory(_root);

    [Fact]
    public async Task PrepareAsync_RealEngineFixtureProjectAndItem_CapturesOnceAndPublishesCreateOnly()
    {
        var projectRoot = Path.Combine(_root, "generated");
        var application = Application(projectRoot);
        WritePackage();

        await using (var project = await application.PrepareAsync(Request(TemplateType.Project, projectRoot), CancellationToken.None))
        {
            Directory.Exists(projectRoot).Should().BeFalse();
            project.Preview().Configuration.Should().NotBeNull();
            await project.CommitAsync(CancellationToken.None);
        }

        File.ReadAllText(Path.Combine(projectRoot, ".func", "config.json")).Should().Contain("\"runtime\": \"fixture\"");
        await using (var item = await application.PrepareAsync(
            Request(TemplateType.Item, projectRoot, ["--Message", "non-default"], "ChosenFunction"), CancellationToken.None))
        {
            File.Exists(Path.Combine(projectRoot, "ChosenFunction.txt")).Should().BeFalse();
            Encoding.UTF8.GetString(item.Preview().Entries.Single(entry => entry.RelativePath == "ChosenFunction.txt").Content.AsSpan())
                .Should().Be("non-default");
            await item.CommitAsync(CancellationToken.None);
        }

        File.ReadAllText(Path.Combine(projectRoot, "ChosenFunction.txt")).Should().Be("non-default");
    }

    [Theory]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("different")]
    public async Task PrepareAsync_RealEngineCanonicalStringParameter_PreservesExplicitValue(string value)
    {
        WritePackage();
        var target = Path.Combine(_root, "target");

        await using var operation = await Application(target).PrepareAsync(
            Request(TemplateType.Item, target, ["--Message=" + value], "Named"), CancellationToken.None);

        Encoding.UTF8.GetString(operation.Preview().Entries.Single(entry => entry.RelativePath == "Named.txt").Content.AsSpan())
            .Should().Be(value);
    }

    [Theory]
    [InlineData("dotnetcli", "Restricted")]
    [InlineData("func", "NotEvaluated")]
    public async Task PrepareAsync_NativeEligibilityIsNotEligible_RejectsDespiteSupportedSyntheticFacts(string host, string expected)
    {
        WritePackage(host: host, extraConstraint: expected == "NotEvaluated");
        var target = Path.Combine(_root, "target");

        var action = () => Application(target).PrepareAsync(Request(TemplateType.Item, target), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.NativeIneligible);
        failure.Message.Should().Contain(expected);
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task OpenAsync_OriginalArchivePathChanges_UsesOnlyRetainedBytesAndExactDescriptor()
    {
        WritePackage();
        var request = Request(TemplateType.Item, Path.Combine(_root, "target"));
        using var package = await new LocalTemplatePackageIntake(_files, _limits).IntakeAsync(request.Package, CancellationToken.None);
        File.WriteAllText(request.Package.ArchivePath, "source path no longer contains a package");
        await using var session = await Adapter().OpenAsync(package,
            new(WorkingDirectory.FromExplicit(request.TargetDirectory)), CancellationToken.None);
        var candidate = session.Candidates.Single(candidate => candidate.Descriptor.Metadata.Identity == "Native.Item");
        var arguments = new TemplateArgumentParser().Parse(candidate.Descriptor, ["--Message", "retained"], []);
        var equivalent = new TemplateDescriptor(candidate.Descriptor.Metadata, candidate.Descriptor.Requirements.IsDeclared
            ? """{"type":"func-project-capabilities","args":["publisher.future-model.http"]}""" : null);
        var substituted = new TemplateArgumentParser().Parse(equivalent, [], []);

        var capture = await candidate.RenderAsync(arguments, "Retained", CancellationToken.None);
        var wrong = () => candidate.RenderAsync(substituted, "Wrong", CancellationToken.None);

        Encoding.UTF8.GetString(capture.Entries.Single(entry => entry.RelativePath == "Retained.txt").Content.AsSpan()).Should().Be("retained");
        await wrong.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PrepareAsync_AbsentNativeCarrier_IsIncompatibleRatherThanImplicitlyCompatible()
    {
        WritePackage(declareRequirement: false);
        var target = Path.Combine(_root, "target");

        var action = () => Application(target).PrepareAsync(Request(TemplateType.Item, target), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.ProjectIncompatible);
        failure.Compatibility!.Diagnostics.Should().Contain(entry => entry.Code == "requirements-absent");
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task PrepareAsync_DuplicateOriginalMetadata_FailsBeforeNativeStateIsAllocated()
    {
        var data = TemplatePackageTestData.Archive(
            [new("content/item/.template.config/template.json",
                """{"identity":"x","name":"x","shortName":"x","tags":{"type":"item"},"funcProjectCapabilities":{},"funcProjectCapabilit\u0069es":{}}""")]);
        var (intake, _) = TemplatePackageTestData.Intake(data);
        using var lease = await intake.IntakeAsync(TemplatePackageTestData.Request(), CancellationToken.None);
        var fileSystem = Substitute.For<IFileSystem>();
        var adapter = new NativePackageTemplateAdapter(fileSystem, new([]), _limits);

        var action = () => adapter.OpenAsync(lease, new(WorkingDirectory.FromExplicit(_root)), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidTemplateMetadataException>().WithMessage("*Duplicate or ambiguous*");
        fileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_ProjectConfigIntentReferencesAnotherRoot_RejectsWithoutPublication()
    {
        WritePackage(primaryOutput: "other.txt");
        var target = Path.Combine(_root, "target");

        var action = () => Application(target).PrepareAsync(Request(TemplateType.Project, target), CancellationToken.None);

        await action.Should().ThrowAsync<InvalidTemplateMetadataException>().WithMessage("*primary output*");
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task PrepareAsync_NativeOutputTraversal_IsRejectedWithoutWritingOutsidePrivateOutput()
    {
        WritePackage(itemExtra: ""","sources":[{"source":"./","target":"../../escape"}]""");
        var target = Path.Combine(_root, "target");

        var action = () => Application(target).PrepareAsync(Request(TemplateType.Item, target), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<InvalidTemplateMetadataException>()).Which;
        failure.Message.Should().Contain("content/item/.template.config/template.json").And.Contain("target").And.Contain("../../escape");
        failure.InnerException.Should().BeOfType<ArgumentException>();
        Directory.Exists(target).Should().BeFalse();
        Directory.Exists(Path.Combine(_root, "escape")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true, "capability-missing")]
    [InlineData(false, "capability-unsupported")]
    public async Task PrepareAsync_RealNativeItemRequiresMissingOrUnsupportedCapability_RejectsBeforeRendering(
        bool missing, string diagnostic)
    {
        WritePackage();
        var target = Path.Combine(_root, "target");
        var project = new FixtureProject(missing ? [] : [new(PackageTemplateFixture.Capability, ProjectCapabilitySupport.Unsupported)]) { Root = target };

        var action = () => Application(target, project).PrepareAsync(Request(TemplateType.Item, target), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.ProjectIncompatible);
        failure.Compatibility!.Diagnostics.Should().Contain(entry => entry.Code == diagnostic);
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task CommitAsync_ArchiveChangesAfterPreview_PublishesRetainedOutputAndProjectCompatibility()
    {
        WritePackage();
        var target = Path.Combine(_root, "target");
        var project = new FixtureProject { Root = target };
        await using var operation = await Application(target, project).PrepareAsync(Request(TemplateType.Item, target, name: "Retained"), CancellationToken.None);
        var retained = operation.Preview().Entries;
        File.WriteAllText(Path.Combine(_root, "package.nupkg"), "not the retained archive");

        var completion = await operation.CommitAsync(CancellationToken.None);

        completion.Status.Should().Be(PreparedCommitStatus.Succeeded);
        operation.Preview().Compatibility!.Project.Should().BeSameAs(project);
        retained.Select(entry => entry.RelativePath).Should().BeEquivalentTo(["Retained.txt", "flag.txt"]);
        retained.Should().OnlyContain(entry => entry.Kind == PreparedOutputKind.File);
        foreach (var entry in retained)
        {
            File.ReadAllBytes(Path.Combine(target, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar))).Should().Equal(entry.Content);
        }
    }

    [Fact]
    public async Task CommitAsync_ExistingItemParentDirectories_AreNotMisreportedAsNewEmptyOutputs()
    {
        WritePackage(itemPath: "src/functions/FunctionName.txt");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "src", "functions"));

        await using var operation = await Application(target).PrepareAsync(Request(TemplateType.Item, target, name: "Nested"), CancellationToken.None);
        await operation.CommitAsync(CancellationToken.None);

        File.ReadAllText(Path.Combine(target, "src", "functions", "Nested.txt")).Should().Be("default");
        operation.Preview().Entries.Should().OnlyContain(entry => entry.Kind == PreparedOutputKind.File);
    }

    [Fact]
    public async Task PrepareAsync_ExplicitFalseCanonicalValue_ReachesTheNativeBooleanParameter()
    {
        WritePackage();
        var target = Path.Combine(_root, "target");

        await using var operation = await Application(target).PrepareAsync(
            Request(TemplateType.Item, target, ["--Flag=false"]), CancellationToken.None);

        Encoding.UTF8.GetString(operation.Preview().Entries.Single(entry => entry.RelativePath == "flag.txt").Content.AsSpan())
            .Should().Be("False");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("CON")]
    public async Task PrepareAsync_InvalidNativeName_HasTargetedInputFailureBeforeRendering(string name)
    {
        WritePackage();
        var target = Path.Combine(_root, "target");

        var action = () => Application(target).PrepareAsync(Request(TemplateType.Item, target, name: name), CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.Should().Be(TemplatePreparationFailure.InvalidNativeInput);
        failure.InnerException.Should().BeAssignableTo<ArgumentException>();
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task RenderAsync_AfterNativeSessionDisposal_RefusesTheRetainedCandidate()
    {
        WritePackage();
        var request = Request(TemplateType.Item, Path.Combine(_root, "target"));
        using var lease = await new LocalTemplatePackageIntake(_files, _limits).IntakeAsync(request.Package, CancellationToken.None);
        var session = await Adapter().OpenAsync(lease, new(WorkingDirectory.FromExplicit(request.TargetDirectory)), CancellationToken.None);
        var candidate = session.Candidates.Single(candidate => candidate.Descriptor.Metadata.Type == TemplateType.Item);
        var arguments = new TemplateArgumentParser().Parse(candidate.Descriptor, [], []);
        await session.DisposeAsync();

        var action = () => candidate.RenderAsync(arguments, "AfterDisposal", CancellationToken.None);

        await action.Should().ThrowAsync<ObjectDisposedException>();
        await session.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_CanceledBeforeNativeWork_DoesNotCreatePrivateState()
    {
        var (intake, _) = TemplatePackageTestData.Intake(TemplatePackageTestData.Archive());
        using var lease = await intake.IntakeAsync(TemplatePackageTestData.Request(), CancellationToken.None);
        var files = Substitute.For<IFileSystem>();
        var adapter = new NativePackageTemplateAdapter(files, new([]), _limits);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var action = () => adapter.OpenAsync(lease, new(WorkingDirectory.FromExplicit(_root)), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        files.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_AuthoredNonzeroPrimaryOutputIndex_DerivesTheActualNestedFunctionsRoot()
    {
        var configuration = """
            {
              "identity":"Native.Project","name":"Indexed project","shortName":"indexed","tags":{"type":"project"},
              "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}},
              "primaryOutputs":[{"path":"unrelated/readme.txt"},{"path":"app/host.json"}],
              "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56","continueOnError":false,
                "args":{"primaryOutputIndex":"1","stack":"fixture","language":"Fixture"}}]
            }
            """;
        File.WriteAllBytes(Path.Combine(_root, "package.nupkg"), TemplatePackageTestData.Archive(
        [
            new("content/project/.template.config/template.json", configuration),
            new("content/project/unrelated/readme.txt", "not the Functions root"),
            new("content/project/app/host.json", """{"version":"2.0"}"""),
        ]));
        var target = Path.Combine(_root, "indexed");

        await using var operation = await Application(target).PrepareAsync(Request(TemplateType.Project, target), CancellationToken.None);
        await operation.CommitAsync(CancellationToken.None);

        operation.Preview().Configuration!.ProjectRootRelativePath.Should().Be("app");
        File.Exists(Path.Combine(target, "app", ".func", "config.json")).Should().BeTrue();
        File.Exists(Path.Combine(target, "unrelated", ".func", "config.json")).Should().BeFalse();
    }

    [Fact]
    public async Task OpenAsync_ArchiveRootTemplate_BindsItsExactOriginalMetadata()
    {
        var configuration = """
            {"identity":"Root.Item","name":"Root item","shortName":"root-item","tags":{"type":"item"},
             "constraints":{"host":{"type":"host","args":[{"hostname":"func"}]}}}
            """;
        File.WriteAllBytes(Path.Combine(_root, "package.nupkg"), TemplatePackageTestData.Archive(
        [
            new(".template.config/template.json", configuration),
            new("root.txt", "root template content"),
        ]));
        var request = Request(TemplateType.Item, Path.Combine(_root, "target"));
        using var lease = await new LocalTemplatePackageIntake(_files, _limits).IntakeAsync(request.Package, CancellationToken.None);

        await using var session = await Adapter().OpenAsync(lease, new(WorkingDirectory.FromExplicit(request.TargetDirectory)), CancellationToken.None);

        session.Candidates.Should().ContainSingle().Which.Descriptor.Metadata.Identity.Should().Be("Root.Item");
    }

    [Theory]
    [InlineData(false, "Default")]
    [InlineData(true, "User")]
    public void CreateInput_DefaultAndEqualExplicitValue_RetainsTheirDifferentNativeSources(bool explicitlySupplied, string expectedSource)
    {
        var nativeParameter = Substitute.For<ITemplateParameter>();
        nativeParameter.Name.Returns("Message");
        var native = Substitute.For<ITemplateInfo>();
        var definitions = new ParameterDefinitionSet([nativeParameter]);
        native.ParameterDefinitions.Returns(definitions);
        var descriptor = new TemplateDescriptor(SelectionFixtures.Metadata() with
        {
            Parameters = [SelectionFixtures.Parameter("Message", defaultValue: "same")],
        });
        var arguments = new TemplateArgumentParser().Parse(descriptor, explicitlySupplied ? ["--Message", "same"] : [], []);

        var input = NativePackageTemplateAdapter.CreateInput(native, arguments);

        input.Values.Should().ContainSingle().Which.DataSource.ToString().Should().Be(expectedSource);
        input.Values.Single().Value.Should().Be("same");
        input.Values.Single().InputDataState.Should().Be(InputDataState.Set);
    }

    [Fact]
    public async Task PrepareAsync_RequiredParameterWithAuthoredDefault_RemainsCompleteWithoutInventingExplicitUserInput()
    {
        WritePackage(requiredMessage: true);
        var target = Path.Combine(_root, "required-default");

        await using var operation = await Application(target).PrepareAsync(Request(TemplateType.Item, target, name: "Defaulted"), CancellationToken.None);

        Encoding.UTF8.GetString(operation.Preview().Entries.Single(entry => entry.RelativePath == "Defaulted.txt").Content.AsSpan())
            .Should().Be("default");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public async Task Invoke_AuthoredTraversal_HasMemberAndPropertyContextAtTheNativeMetadataBoundary(string property)
    {
        WritePackage(itemExtra: ",\"sources\":[{\"" + property + "\":\"../../escape\"}]");
        var target = Path.Combine(_root, "traversal");
        var command = new PackageTemplateNewCommand(Application(target), new(new TestInteractionService()));

        var action = () => command.Parse(CommandArguments(target))
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var surfaced = (await action.Should().ThrowAsync<GracefulException>()).Which;
        var metadata = surfaced.InnerException.Should().BeOfType<InvalidTemplateMetadataException>().Which;
        metadata.Message.Should().Contain("content/item/.template.config/template.json").And.Contain(property).And.Contain("../../escape");
        metadata.InnerException.Should().BeOfType<ArgumentException>();
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_MetadataDecodingFailure_PreservesStrictDecoderAndExactArchiveMember()
    {
        WritePackage();
        const string member = "content/item/.template.config/template.json";
        string archivePath = Path.Combine(_root, "package.nupkg");
        using MemoryStream retained = new();
        retained.Write(File.ReadAllBytes(archivePath));
        using (ZipArchive archive = new(retained, ZipArchiveMode.Update, leaveOpen: true))
        {
            var original = archive.GetEntry(member)!;
            var timestamp = original.LastWriteTime;
            int attributes = original.ExternalAttributes;
            original.Delete();
            var replacement = archive.CreateEntry(member, CompressionLevel.NoCompression);
            replacement.LastWriteTime = timestamp;
            replacement.ExternalAttributes = attributes;
            using var content = replacement.Open();
            content.Write([0x7b, 0xff, 0x7d]);
        }

        File.WriteAllBytes(archivePath, retained.ToArray());
        var target = Path.Combine(_root, "decoding");
        var command = new PackageTemplateNewCommand(Application(target), new(new TestInteractionService()));

        var action = () => command.Parse(CommandArguments(target))
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var surfaced = (await action.Should().ThrowAsync<GracefulException>()).Which;
        var metadata = surfaced.InnerException.Should().BeOfType<InvalidTemplateMetadataException>().Which;
        metadata.Message.Should().Contain(member);
        metadata.InnerException.Should().BeOfType<DecoderFallbackException>();
        Directory.Exists(target).Should().BeFalse();
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("entries")]
    [InlineData("path")]
    public async Task Invoke_CaptureProfileRejection_HasPreparationCategoryAndSelectedMemberContext(string limit)
    {
        WritePackage();
        var target = Path.Combine(_root, "capture-" + limit);
        var limits = TemplatePackageTestData.Limits(
            entryBytes: limit == "bytes" ? 4096 : 1_000_000,
            entries: limit == "entries" ? 1 : 100,
            pathLength: limit == "path" ? 8 : 256);
        var command = new PackageTemplateNewCommand(Application(target, nativeLimits: limits), new(new TestInteractionService()));

        var action = () => command.Parse([.. CommandArguments(target), "--", "--Message=" + new string('x', 4097)])
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var surfaced = (await action.Should().ThrowAsync<GracefulException>()).Which;
        var preparation = surfaced.InnerException.Should().BeOfType<TemplatePreparationException>().Which;
        preparation.Failure.ToString().Should().Be("CaptureRejected");
        preparation.Message.Should().Contain("content/item/.template.config/template.json").And.Contain(".txt");
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task PrepareAsync_CaptureEntryExactlyAtByteLimit_IsRetained()
    {
        WritePackage();
        var target = Path.Combine(_root, "capture-boundary");

        await using var operation = await Application(target, nativeLimits: TemplatePackageTestData.Limits(entryBytes: 4096))
            .PrepareAsync(Request(TemplateType.Item, target, ["--Message=" + new string('x', 4096)], "Bounded"), CancellationToken.None);

        operation.Preview().Entries.Single(entry => entry.RelativePath == "Bounded.txt").Content.Should().HaveCount(4096);
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task Invoke_CaptureUnsafeOutput_HasPreparationDiagnosticWithoutARealLinkProbe()
    {
        WritePackage();
        var target = Path.Combine(_root, "unsafe-output");
        var files = ForwardingNativeFiles(path => path.EndsWith("Bounded.txt", StringComparison.Ordinal)
            ? FileAttributes.ReparsePoint : _files.GetAttributesIfExists(path));
        var command = new PackageTemplateNewCommand(Application(target, nativeFiles: files), new(new TestInteractionService()));

        var action = () => command.Parse(CommandArguments(target))
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var surfaced = (await action.Should().ThrowAsync<GracefulException>()).Which;
        var preparation = surfaced.InnerException.Should().BeOfType<TemplatePreparationException>().Which;
        preparation.Failure.ToString().Should().Be("CaptureRejected");
        preparation.Message.Should().Contain("content/item/.template.config/template.json").And.Contain("Bounded.txt");
        Directory.Exists(target).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invoke_CaptureProgrammerFailure_IsNotConvertedIntoANativeDataDiagnostic(bool argumentFailure)
    {
        WritePackage();
        var target = Path.Combine(_root, "programmer-failure");
        Exception failure = argumentFailure ? new ArgumentException("Programmer marker.") : new InvalidOperationException("Programmer marker.");
        var files = ForwardingNativeFiles(_ => throw failure);
        var command = new PackageTemplateNewCommand(Application(target, nativeFiles: files), new(new TestInteractionService()));

        var action = () => command.Parse(CommandArguments(target))
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var observed = (await action.Should().ThrowAsync<Exception>()).Which;
        observed.Should().BeSameAs(failure);
        Directory.Exists(target).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenAsync_OpeningFailureOrCancellationAndCleanupFailure_RetainsBothOutcomes(bool canceled)
    {
        WritePackage();
        using var cancellation = new CancellationTokenSource();
        var request = Request(TemplateType.Item, Path.Combine(_root, "opening"));
        using var lease = await new LocalTemplatePackageIntake(_files, _limits).IntakeAsync(request.Package, CancellationToken.None);
        var files = Substitute.For<IFileSystem>();
        string workspace = Path.Combine(_root, "uncreated-private-opening");
        files.CreateTempDirectory().Returns(new TempDirectory(workspace, files));
        var openingFailure = new IOException("Opening marker.");
        var cleanupFailure = new IOException("Cleanup marker.");
        files.CreateNewFile(Arg.Any<string>()).Returns(_ =>
        {
            if (canceled)
            {
                cancellation.Cancel();
                throw new OperationCanceledException("Opening cancellation marker.", cancellation.Token);
            }

            throw openingFailure;
        });
        files.When(fileSystem => fileSystem.DeleteDirectory(workspace)).Do(_ => throw cleanupFailure);
        var adapter = Adapter(files);

        var action = () => adapter.OpenAsync(lease, new(WorkingDirectory.FromExplicit(request.TargetDirectory)), cancellation.Token);

        var failure = (await action.Should().ThrowAsync<AggregateException>()).Which;
        failure.InnerExceptions.Should().HaveCount(2);
        if (canceled)
        {
            failure.InnerExceptions[0].Should().BeAssignableTo<OperationCanceledException>()
                .Which.CancellationToken.Should().Be(cancellation.Token);
        }
        else
        {
            failure.InnerExceptions[0].Should().BeSameAs(openingFailure);
        }

        failure.InnerExceptions[1].Should().BeSameAs(cleanupFailure);
        files.Received(1).DeleteDirectory(workspace);
    }

    [Theory]
    [InlineData("Native.Item", "python-append-helper", false, true)]
    [InlineData("native-item", "private-helper", false, true)]
    [InlineData("Native.Item", "private-helper", true, true)]
    [InlineData("Native.Item", "python-append-helper", false, false)]
    public async Task Invoke_InternalRole_RefusesOrdinarySelectionBeforeCompatibilityOrPublication(
        string reference, string role, bool hidden, bool declareRequirement)
    {
        WritePackage(declareRequirement: declareRequirement, itemTags: $",\"func-role\":\"{role}\"",
            itemHost: hidden ? """{"isHidden":true}""" : null);
        var target = Path.Combine(_root, "internal-item");
        var project = new FixtureProject { Root = target };
        if (declareRequirement)
        {
            var compatibility = new ItemTemplateCompatibilityMatcher().Evaluate(
                project, new([PackageTemplateFixture.Capability]), CancellationToken.None);
            compatibility.Compatibility.Should().Be(ItemTemplateCompatibility.Compatible);
        }

        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(Application(target, project), new(interaction));

        var action = () => command.Parse(CommandArguments(target, reference))
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        var failure = (await action.Should().ThrowAsync<GracefulException>()).Which;
        var preparation = failure.InnerException.Should().BeOfType<TemplatePreparationException>().Which;
        preparation.Failure.ToString().Should().Be("InternalTemplate");
        preparation.Message.Should().Contain("internal role").And.Contain("ordinary function template");
        Directory.Exists(target).Should().BeFalse();
        interaction.Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_InternalRole_RemainsDeniedByTheProjectedEligibleCandidate()
    {
        WritePackage(itemTags: ",\"func-r\\u006fle\":\"private-helper\"");
        var request = Request(TemplateType.Item, Path.Combine(_root, "projected-internal-item"));
        using var package = await new LocalTemplatePackageIntake(_files, _limits).IntakeAsync(request.Package, CancellationToken.None);
        await using var session = await Adapter().OpenAsync(package,
            new(WorkingDirectory.FromExplicit(request.TargetDirectory)), CancellationToken.None);
        var candidate = session.Candidates.Single(candidate => candidate.Descriptor.Metadata.Identity == "Native.Item");
        candidate.Eligibility.Should().Be(FuncTemplateConstraintOutcome.Eligible);
        candidate.Descriptor.Requirements.CapabilityIds.Should().Equal(PackageTemplateFixture.Capability);
        var arguments = new TemplateArgumentParser().Parse(candidate.Descriptor, [], []);

        var action = () => candidate.RenderAsync(arguments, "Denied", CancellationToken.None);

        var failure = (await action.Should().ThrowAsync<TemplatePreparationException>()).Which;
        failure.Failure.ToString().Should().Be("InternalTemplate");
        Directory.Exists(request.TargetDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invoke_OrdinaryItemWithoutInternalRole_PreservesExplicitInvocationEvenWhenHidden(bool hidden)
    {
        WritePackage(itemHost: hidden ? """{"isHidden":true}""" : null);
        var target = Path.Combine(_root, "ordinary-item");
        var interaction = new TestInteractionService();
        var command = new PackageTemplateNewCommand(Application(target), new(interaction));

        int exit = await command.Parse(CommandArguments(target))
            .InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exit.Should().Be(0);
        File.ReadAllText(Path.Combine(target, "Bounded.txt")).Should().Be("default");
        interaction.Lines.Should().ContainSingle().Which.Should().StartWith("SUCCESS:");
    }

    private NativePackageTemplateAdapter Adapter(IFileSystem? files = null, TemplatePackageIntakeLimits? limits = null)
        => new(files ?? _files, new([new FixtureStack()]), limits ?? _limits);

    private PackageTemplateApplication Application(
        string target, FixtureProject? project = null, IFileSystem? nativeFiles = null, TemplatePackageIntakeLimits? nativeLimits = null)
    {
        var resolver = Substitute.For<IFunctionsProjectResolver>();
        project ??= new FixtureProject { Root = target };
        resolver.ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ProjectResolutionResult>(new ProjectResolutionResult.Resolved(project, "Synthetic runtime for native adapter fixture.")));
        return new(new(_files, _limits), Adapter(nativeFiles, nativeLimits), new(new()), resolver, new(), _files);
    }

    private IFileSystem ForwardingNativeFiles(Func<string, FileAttributes?>? attributes = null)
    {
        var files = Substitute.For<IFileSystem>();
        files.CreateTempDirectory().Returns(_ => _files.CreateTempDirectory());
        files.CreateNewFile(Arg.Any<string>()).Returns(call => _files.CreateNewFile(call.Arg<string>()));
        files.CreateStagingDirectory(Arg.Any<string>()).Returns(call => _files.CreateStagingDirectory(call.Arg<string>()));
        files.ComputeSha256Async(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _files.ComputeSha256Async(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));
        files.OpenReadIfExists(Arg.Any<string>()).Returns(call => _files.OpenReadIfExists(call.Arg<string>()));
        files.GetAttributesIfExists(Arg.Any<string>()).Returns(call =>
            attributes is null ? _files.GetAttributesIfExists(call.Arg<string>()) : attributes(call.Arg<string>()));
        files.When(fileSystem => fileSystem.DeleteDirectory(Arg.Any<string>())).Do(call => _files.DeleteDirectory(call.Arg<string>()));
        return files;
    }

    private string[] CommandArguments(string target, string reference = "Native.Item") =>
    [
        target, "--package", Path.Combine(_root, "package.nupkg"),
        "--package-id", TemplatePackageTestData.PackageId, "--package-version", TemplatePackageTestData.PackageVersion,
        "--package-sha256", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "package.nupkg")))),
        "--template", reference, "--name", "Bounded",
    ];

    private PackageTemplateRequest Request(TemplateType type, string target, string[]? tokens = null, string? name = null)
        => new(new(Path.Combine(_root, "package.nupkg"), TemplatePackageTestData.PackageId, TemplatePackageTestData.PackageVersion, "FuncTemplate"),
            new(type == TemplateType.Project ? "Native.Project" : "Native.Item", type, [.. tokens ?? []], []),
            target, name);

    private void WritePackage(
        string host = "func", bool extraConstraint = false, bool declareRequirement = true, string primaryOutput = "host.json",
        string itemExtra = "", string itemPath = "FunctionName.txt", bool requiredMessage = false,
        string itemTags = "", string? itemHost = null)
    {
        string extra = extraConstraint ? ""","unknown":{"type":"not-a-native-factory","args":[]}""" : "";
        string requirement = declareRequirement
            ? ""","funcProjectCapabilities":{"type":"func-project-capabilities","args":["publisher.future-model.http"]}""" : "";
        string constraints = $$$"""
            "constraints":{"host":{"type":"host","args":[{"hostname":"{{{host}}}"}]}{{{extra}}}}
            """;
        string project = $$$"""
            {
              "identity":"Native.Project","name":"Native project","shortName":"native-project","tags":{"type":"project"},
              {{{constraints}}},
              "primaryOutputs":[{"path":"{{{primaryOutput}}}"}],
              "postActions":[{"actionId":"00e33184-ffc7-43ef-bec0-b1684df8ad56","continueOnError":false,
                "args":{"primaryOutputIndex":"0","stack":"fixture","language":"Fixture"}}]
            }
            """;
        string item = $$$"""
            {
              "identity":"Native.Item","name":"Native item","shortName":"native-item","tags":{"type":"item"{{{itemTags}}}},
              "sourceName":"FunctionName",{{{constraints}}},
              "symbols":{
                "Message":{"type":"parameter","datatype":"string","defaultValue":"default","replaces":"__MESSAGE__","isRequired":{{{(requiredMessage ? "true" : "false")}}}},
                "Flag":{"type":"parameter","datatype":"bool","defaultValue":"true","replaces":"__FLAG__"}
              }
              {{{requirement}}}
              {{{itemExtra}}}
            }
            """;
        List<TemplatePackageTestData.Entry> entries =
        [
            new("content/project/.template.config/template.json", project),
            new("content/project/host.json", """{"version":"2.0"}"""),
            new("content/item/.template.config/template.json", item),
            new("content/item/" + itemPath, "__MESSAGE__"),
            new("content/item/flag.txt", "__FLAG__"),
        ];
        if (itemHost is not null)
        {
            entries.Add(new("content/item/.template.config/func.host.json", itemHost));
        }

        File.WriteAllBytes(Path.Combine(_root, "package.nupkg"), TemplatePackageTestData.Archive(entries));
    }

    private sealed class FixtureStack : IProjectStack
    {
        public string Stack => "fixture";
        public string DisplayName => "Native adapter fixture";
        public IReadOnlyList<string> WorkerRuntimeAliases => [];
        public IReadOnlyList<string> SupportedLanguages => ["Fixture"];
        public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases => new Dictionary<string, IReadOnlyList<string>>();
    }
}
