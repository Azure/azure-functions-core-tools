// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using AwesomeAssertions.Specialized;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node.Tests;

public class NodeProjectCapabilityTests
{
    private const string Capability = "azure.functions.node.model-v4.javascript-commonjs";
    private const string Package = """{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js"}""";
    private static readonly string[] _expectedPaths =
    [
        "package.json", "host.json", "tsconfig.json", ".func/config.json",
        "local.settings.json", "src/package.json", "src/functions/package.json",
    ];

    private static string Root => Path.DirectorySeparatorChar == '\\' ? @"C:\projects\node" : "/projects/node";

    [Theory]
    [InlineData(null)]
    [InlineData("commonjs")]
    public async Task Capture_CompleteCommonJsProfile_ReportsOneSupportedCapability(string? mode)
    {
        var files = new FakeFileReader();
        if (mode is not null)
        {
            files.Put("package.json", Package[..^1] + ""","type":"commonjs"}""");
        }

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().ContainSingle().Which.Id.Should().Be(Capability);
        facts.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
        facts.WorkingDirectory.Info.FullName.Should().Be(Root);
        files.ReadPaths.Should().Equal(_expectedPaths);
        files.ReadRoots.Should().OnlyContain(root => root == Root);
    }

    [Fact]
    public async Task Capture_CorroboratingConfiguration_UsesSurfaceSpecificComparisonsAndJsonComments()
    {
        var files = new FakeFileReader();
        files.Put(".func/config.json", """{/* project */"STACK":{"RUNTIME":"NODE","LANGUAGE":"javascript",},}""");
        files.Put("local.settings.json", """{"IsEncrypted":false,"Values":{"functions_worker_runtime":"NODE",},}""");
        files.Put("host.json", """{/* host */"version":"2.0",}""");

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
    }

    [Theory]
    [InlineData("""{"stack:runtime":"python"}""", false)]
    [InlineData("""{"stack:runtime":"python"}""", true)]
    [InlineData("""{"stack:language":"TypeScript"}""", false)]
    [InlineData("""{"stack:language":"TypeScript"}""", true)]
    [InlineData("""{"STACK:RUNTIME":"python"}""", false)]
    [InlineData("""{"STACK:RUNTIME":"python"}""", true)]
    [InlineData("""{"StAcK:LaNgUaGe":"TypeScript"}""", false)]
    [InlineData("""{"StAcK:LaNgUaGe":"TypeScript"}""", true)]
    [InlineData("""{"\u0073tack\u003arun\u0074ime":"python"}""", false)]
    [InlineData("""{"stack\u003alanguage":"TypeScript"}""", true)]
    [InlineData("""{"stack:runtime":"node"}""", false)]
    [InlineData("""{"stack:runtime":"node"}""", true)]
    [InlineData("""{"stack:language":"JavaScript"}""", false)]
    [InlineData("""{"stack:language":"JavaScript"}""", true)]
    [InlineData("""{"stack":{"language":"JavaScript"},"STACK:RUNTIME":"node"}""", false)]
    [InlineData("""{"stack":{"runtime":"node"},"STACK:LANGUAGE":"JavaScript"}""", true)]
    [InlineData("""{"stack:runtime:custom":"python"}""", false)]
    [InlineData("""{"STACK:LANGUAGE:custom":"TypeScript"}""", true)]
    [InlineData("""{"stack":{"runtime:custom":"python"}}""", false)]
    [InlineData("""{"STACK":{"LANGUAGE\u003acustom":"TypeScript"}}""", true)]
    public async Task NrfP01_NoncanonicalCliStackDeclarations_DoNotReportSupport(string json, bool module)
    {
        var files = new FakeFileReader();
        files.Put(".func/config.json", json);
        if (module)
        {
            files.Put("package.json", Package[..^1] + ""","type":"module"}""");
        }

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().BeEmpty();
        files.ReadPaths.Should().Equal(_expectedPaths);
    }

    [Theory]
    [InlineData("""{"stack":{"runtime":"node"},"STACK:RUNTIME":"python"}""", false)]
    [InlineData("""{"STACK:RUNTIME":"python","stack":{"runtime":"node"}}""", false)]
    [InlineData("""{"stack":{"runtime":"node"},"STACK:RUNTIME":"python"}""", true)]
    [InlineData("""{"STACK:RUNTIME":"python","stack":{"runtime":"node"}}""", true)]
    [InlineData("""{"stack":{"language":"JavaScript"},"stack:language":"TypeScript"}""", false)]
    [InlineData("""{"stack:language":"TypeScript","stack":{"language":"JavaScript"}}""", true)]
    [InlineData("""{"STACK":{"RUNTIME":"node"},"stack:runtime":"node"}""", false)]
    [InlineData("""{"stack:runtime":"node","STACK":{"RUNTIME":"node"}}""", true)]
    [InlineData("""{"stack":{"runtime":"node"},"\u0073tack\u003aruntime":"node"}""", false)]
    [InlineData("""{"stack:runtime":"node","STACK:RUNTIME":"python"}""", true)]
    [InlineData("""{"STACK:RUNTIME":"python","stack:runtime":"node"}""", false)]
    [InlineData("""{"stack:language":"JavaScript","stack\u003alanguage":"JavaScript"}""", true)]
    public async Task NrfP01_EquivalentCliStackDeclarations_FailContextually(string json, bool module)
    {
        var files = new FakeFileReader();
        files.Put(".func/config.json", json);
        if (module)
        {
            files.Put("package.json", Package[..^1] + ""","type":"module"}""");
        }

        ExceptionAssertions<FormatException> error = await FluentActions.Awaiting(() => CaptureAsync(files))
            .Should().ThrowAsync<FormatException>();

        error.Which.Message.Should().Contain(".func/config.json").And.Contain("stack:");
        files.ReadPaths.Should().Equal(_expectedPaths);
    }

    [Theory]
    [InlineData("""{"stack:runtime":null}""", false)]
    [InlineData("""{"stack:runtime":false}""", true)]
    [InlineData("""{"STACK:LANGUAGE":4}""", false)]
    [InlineData("""{"stack:language":[]}""", true)]
    [InlineData("""{"stack\u003aruntime":{"value":"python"}}""", true)]
    [InlineData("""{"stack":{"language":"JavaScript"},"stack:language":null}""", false)]
    public async Task NrfP01_MalformedFlatCliStackValues_FailContextually(string json, bool module)
    {
        var files = new FakeFileReader();
        files.Put(".func/config.json", json);
        if (module)
        {
            files.Put("package.json", Package[..^1] + ""","type":"module"}""");
        }

        ExceptionAssertions<FormatException> error = await FluentActions.Awaiting(() => CaptureAsync(files))
            .Should().ThrowAsync<FormatException>();

        error.Which.Message.Should().Contain(".func/config.json").And.Contain("stack:");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"stack":{"runtime":"node","language":"JavaScript"}}""")]
    [InlineData("""{"STACK":{"RUNTIME":"NODE","LANGUAGE":"javascript"}}""")]
    [InlineData("""{"\u0073tack":{"run\u0074ime":"node","language":"JavaScript"}}""")]
    [InlineData("""{"logging:level":"debug","stack:description":"unrelated"}""")]
    [InlineData("""{"stack":{"runtime":"node","runtimeExtra:custom":"unrelated"}}""")]
    public async Task NrfP01_CanonicalOrAbsentCorroboration_PreservesBoundedOutcomes(string? json)
    {
        bool[] modes = [false, true];
        foreach (bool module in modes)
        {
            var files = new FakeFileReader();
            if (json is not null)
            {
                files.Put(".func/config.json", json);
            }

            if (module)
            {
                files.Put("package.json", Package[..^1] + ""","type":"module"}""");
            }

            NodeFunctionsProject facts = await CaptureAsync(files);

            facts.Capabilities[0].Support.Should().Be(module ? ProjectCapabilitySupport.Unsupported : ProjectCapabilitySupport.Supported);
            files.ReadPaths.Should().Equal(_expectedPaths);
        }
    }

    [Theory]
    [InlineData(false, false, ProjectCapabilitySupport.Supported)]
    [InlineData(true, false, ProjectCapabilitySupport.Unsupported)]
    [InlineData(false, true, null)]
    [InlineData(true, true, null)]
    public async Task NrfP01_EachObservation_ReadsAllSevenSlotsWithoutChangingTheEarlierProject(
        bool module, bool flat, ProjectCapabilitySupport? expected)
    {
        foreach (string path in _expectedPaths)
        {
            var files = new FakeFileReader();
            files.Put(".func/config.json", flat ? """{"stack:runtime":"node"}""" : """{"stack":{"runtime":"node"}}""");
            if (module)
            {
                files.Put("package.json", Package[..^1] + ""","type":"module"}""");
            }

            NodeFunctionsProject before = await CaptureAsync(files);
            string changed = files.Entries.TryGetValue(path, out (NodeProjectFileKind Kind, byte[] Bytes) entry)
                ? Encoding.UTF8.GetString(entry.Bytes) + " "
                : "{}";
            files.Put(path, changed);

            NodeFunctionsProject after = await CaptureAsync(files);

            AssertSupport(before, expected);
            AssertSupport(after, path is "tsconfig.json" or "src/package.json" or "src/functions/package.json" ? null : expected);
            after.Should().NotBeSameAs(before);
            files.ReadPaths.Should().Equal(_expectedPaths.Concat(_expectedPaths));
        }
    }

    [Theory]
    [InlineData("""{"main":"src/functions/*.js"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.1.0"},"main":"src/functions/*.js"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"4.0.0"},"main":"src/functions/*.js"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":" ^4.0.0"},"main":"src/functions/*.js"}""")]
    [InlineData("""{"Dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js"}""")]
    [InlineData("""{"devDependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"}}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"dist/src/functions/*.js"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"./src/functions/*.js"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js "}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js","type":"CommonJS"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js","overrides":{}}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js","optionalDependencies":{"@azure/functions":"^3.0.0"}}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js","peerDependencies":{"@azure/functions":"^4.0.0"}}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.1.0"},"main":"src/functions/*.js","type":"module"}""")]
    [InlineData("""{"dependencies":{"@azure/functions":"^4.0.0"},"main":"dist/src/functions/*.js","type":"module"}""")]
    public async Task Capture_UnrecognizedPackageProfile_DoesNotReportSupport(string json)
    {
        var files = new FakeFileReader();
        files.Put("package.json", json);

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().BeEmpty();
        files.ReadPaths.Should().Equal(_expectedPaths);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("host.json")]
    public async Task Capture_MissingRequiredFile_DoesNotReportSupport(string path)
    {
        var files = new FakeFileReader();
        files.Entries.Remove(path);

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_CorroboratingLabelsAlone_DoNotEstablishSupport()
    {
        var files = new FakeFileReader();
        files.Entries.Clear();
        files.Put(".func/config.json", """{"stack":{"runtime":"node","language":"JavaScript"}}""");
        files.Put("local.settings.json", """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().BeEmpty();
    }

    [Theory]
    [InlineData("host.json", "{}")]
    [InlineData("host.json", """{"version":"4.0"}""")]
    [InlineData("tsconfig.json", "{ not interpreted")]
    [InlineData(".func/config.json", """{"stack":{"runtime":"python"}}""")]
    [InlineData(".func/config.json", """{"stack":{"language":"TypeScript"}}""")]
    [InlineData(".func/config.json", """{"stack":{"runtime":""}}""")]
    [InlineData("local.settings.json", """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"python"}}""")]
    [InlineData("local.settings.json", """{"Values":{"FUNCTIONS_WORKER_RUNTIME":""}}""")]
    [InlineData("local.settings.json", """{"IsEncrypted":true,"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""")]
    public async Task Capture_MissingOrContradictoryConfiguration_DoesNotReportSupport(string path, string json)
    {
        var files = new FakeFileReader();
        files.Put(path, json);

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().BeEmpty();
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("host.json")]
    [InlineData("tsconfig.json")]
    [InlineData(".func/config.json")]
    [InlineData("local.settings.json")]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task Capture_DirectoryOrUnsupportedKind_IsNotAbsence(string path)
    {
        NodeProjectFileKind[] kinds = [NodeProjectFileKind.Directory, NodeProjectFileKind.Unsupported];
        foreach (NodeProjectFileKind kind in kinds)
        {
            var files = new FakeFileReader();
            files.Entries[path] = (kind, []);

            NodeFunctionsProject facts = await CaptureAsync(files);

            facts.Capabilities.Should().BeEmpty();
            files.ReadPaths.Should().Equal(_expectedPaths);
        }
    }

    [Theory]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task Capture_NestedBoundaryCreationChangeAndRemoval_AffectsOnlyNewObservations(string path)
    {
        var files = new FakeFileReader();
        NodeFunctionsProject before = await CaptureAsync(files);
        files.Put(path, """{"type":"module"}""");

        NodeFunctionsProject present = await CaptureAsync(files);
        files.Put(path, """{"type":"commonjs"}""");
        NodeFunctionsProject changed = await CaptureAsync(files);
        files.Put(path, "{ not valid JSON");
        NodeFunctionsProject malformed = await CaptureAsync(files);
        files.Entries.Remove(path);
        NodeFunctionsProject removed = await CaptureAsync(files);

        present.Capabilities.Should().BeEmpty();
        changed.Capabilities.Should().BeEmpty();
        malformed.Capabilities.Should().BeEmpty();
        removed.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
        before.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
    }

    [Theory]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task Capture_NestedBoundaryKindChanges_RequireEstablishedAbsence(string path)
    {
        var files = new FakeFileReader();
        foreach (NodeProjectFileKind kind in Enum.GetValues<NodeProjectFileKind>())
        {
            files.Entries[path] = (kind, []);
            NodeFunctionsProject facts = await CaptureAsync(files);

            AssertSupport(facts, kind == NodeProjectFileKind.Absent ? ProjectCapabilitySupport.Supported : null);
        }
    }

    [Fact]
    public async Task Capture_ExplicitModuleWithOtherwiseCompleteScope_IsUnsupported()
    {
        var files = new FakeFileReader();
        files.Put("package.json", Package[..^1] + ""","type":"module"}""");

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Unsupported);
    }

    [Theory]
    [InlineData("src/package.json", """{"type":"commonjs"}""")]
    [InlineData("src/functions/package.json", """{"type":"module"}""")]
    [InlineData("tsconfig.json", "{}")]
    [InlineData("host.json", "{}")]
    public async Task Capture_ModuleWithUnresolvedPrerequisites_DoesNotReportUnsupported(string path, string json)
    {
        var files = new FakeFileReader();
        files.Put("package.json", Package[..^1] + ""","type":"module"}""");
        files.Put(path, json);

        NodeFunctionsProject facts = await CaptureAsync(files);

        facts.Capabilities.Should().BeEmpty();
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("host.json")]
    [InlineData(".func/config.json")]
    [InlineData("local.settings.json")]
    public async Task Capture_MalformedInterpretedJson_FailsWithPathAndOriginalCause(string path)
    {
        var files = new FakeFileReader();
        files.Put(path, "{ broken");

        ExceptionAssertions<FormatException> error = await FluentActions.Awaiting(() => CaptureAsync(files)).Should().ThrowAsync<FormatException>();

        error.Which.Message.Should().Contain(path);
        error.Which.InnerException.Should().BeAssignableTo<JsonException>();
    }

    [Theory]
    [InlineData("package.json", "[]")]
    [InlineData("package.json", """{"dependencies":[]}""")]
    [InlineData("package.json", """{"dependencies":{"@azure/functions":4}}""")]
    [InlineData("package.json", """{"main":null}""")]
    [InlineData("package.json", """{"type":false}""")]
    [InlineData("package.json", """{"main":"a","main":"b"}""")]
    [InlineData("package.json", """{"dependencies":{},"dependencies":{}}""")]
    [InlineData("package.json", """{"dependencies":{"@azure/functions":"^4.0.0","@azure/\u0066unctions":"^3.0.0"}}""")]
    [InlineData("package.json", """{"main":"\uD800"}""")]
    [InlineData("package.json", """{"\uD800":"invalid"}""")]
    [InlineData("host.json", "null")]
    [InlineData("host.json", """{"version":2}""")]
    [InlineData("host.json", """{"version":"2.0","version":"2.0"}""")]
    [InlineData(".func/config.json", """{"stack":null}""")]
    [InlineData(".func/config.json", """{"stack":{},"STACK":{}}""")]
    [InlineData(".func/config.json", """{"stack":{"runtime":"node","RUNTIME":"node"}}""")]
    [InlineData(".func/config.json", """{"stack":{"language":true}}""")]
    [InlineData("local.settings.json", """{"Values":[]}""")]
    [InlineData("local.settings.json", """{"Values":{},"Values":{}}""")]
    [InlineData("local.settings.json", """{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node","functions_worker_runtime":"node"}}""")]
    [InlineData("local.settings.json", """{"Values":{"FUNCTIONS_WORKER_RUNTIME":false}}""")]
    [InlineData("local.settings.json", """{"IsEncrypted":"false"}""")]
    [InlineData("local.settings.json", """{"IsEncrypted":false,"IsEncrypted":true}""")]
    public async Task Capture_AmbiguousOrMalformedInterpretedProperty_FailsContextually(string path, string json)
    {
        var files = new FakeFileReader();
        files.Put(path, json);

        ExceptionAssertions<FormatException> error = await FluentActions.Awaiting(() => CaptureAsync(files)).Should().ThrowAsync<FormatException>();

        error.Which.Message.Should().Contain(path);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("host.json")]
    [InlineData("tsconfig.json")]
    [InlineData(".func/config.json")]
    [InlineData("local.settings.json")]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task Capture_ProbeOrReadFailure_PreservesPathAndCause(string path)
    {
        var files = new FakeFileReader();
        var cause = new IOException("Interrupted evidence read.");
        files.Failures[path] = cause;

        ExceptionAssertions<IOException> error = await FluentActions.Awaiting(() => CaptureAsync(files)).Should().ThrowAsync<IOException>();

        error.Which.Message.Should().Contain(path);
        error.Which.InnerException.Should().BeSameAs(cause);
    }

    [Theory]
    [InlineData("src/package.json")]
    [InlineData("src/functions/package.json")]
    public async Task Capture_UnreadableNestedBoundary_IsNotAbsenceOrAnEmptySnapshot(string path)
    {
        var files = new FakeFileReader();
        var cause = new UnauthorizedAccessException("Evidence access denied.");
        files.Failures[path] = cause;

        ExceptionAssertions<UnauthorizedAccessException> error = await FluentActions.Awaiting(() => CaptureAsync(files)).Should().ThrowAsync<UnauthorizedAccessException>();

        error.Which.Message.Should().Contain(path);
        error.Which.InnerException.Should().BeSameAs(cause);
    }

    [Fact]
    public async Task Capture_RepeatedObservations_CaptureIndependentImmutableProjects()
    {
        var files = new FakeFileReader();
        NodeFunctionsProject first = await CaptureAsync(files);
        NodeFunctionsProject second = await CaptureAsync(files);
        files.Put("host.json", """{ "version": "2.0" }""");
        NodeFunctionsProject changed = await CaptureAsync(files);

        first.Should().NotBeSameAs(second);
        first.Capabilities.Should().BeEquivalentTo(second.Capabilities);
        changed.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
        files.ReadPaths.Should().Equal(Enumerable.Range(0, 3).SelectMany(_ => _expectedPaths));
        FluentActions.Invoking(() => ((IList<ProjectCapability>)first.Capabilities).Clear()).Should().Throw<NotSupportedException>();
        first.Capabilities.Should().BeEquivalentTo(second.Capabilities);
    }

    [Fact]
    public async Task Capture_NestedBoundariesAtEitherDepth_PreventSupport()
    {
        var files = new FakeFileReader();
        files.Put("src/package.json", "{}");
        NodeFunctionsProject first = await CaptureAsync(files);
        files.Entries.Remove("src/package.json");
        files.Put("src/functions/package.json", "{}");

        NodeFunctionsProject relocated = await CaptureAsync(files);

        first.Capabilities.Should().BeEmpty();
        relocated.Capabilities.Should().BeEmpty();
    }

    [Theory]
    [InlineData("package.json", """{ "dependencies":{"@azure/functions":"^4.0.0"},"main":"src/functions/*.js"}""")]
    [InlineData("host.json", """{ "version":"2.0"}""")]
    [InlineData("tsconfig.json", "{}")]
    [InlineData(".func/config.json", "{}")]
    [InlineData("local.settings.json", "{}")]
    [InlineData("src/package.json", "{}")]
    [InlineData("src/functions/package.json", "{}")]
    public async Task Capture_ObservedSlotChanges_DoNotMutateTheEarlierProject(string path, string contents)
    {
        var files = new FakeFileReader();
        NodeFunctionsProject before = await CaptureAsync(files);
        files.Put(path, contents);

        NodeFunctionsProject changed = await CaptureAsync(files);

        before.Capabilities.Should().ContainSingle().Which.Support.Should().Be(ProjectCapabilitySupport.Supported);
        AssertSupport(changed, path is "tsconfig.json" or "src/package.json" or "src/functions/package.json"
            ? null : ProjectCapabilitySupport.Supported);
    }

    [Fact]
    public async Task Capture_SourceBufferChangesDuringLaterReads_UsesTheCapturedBytes()
    {
        var files = new FakeFileReader();
        NodeFunctionsProject before = await CaptureAsync(files);
        byte[] original = files.Entries["package.json"].Bytes;
        files.BeforeRead = (path, _) =>
        {
            if (path == "host.json")
            {
                original[Array.IndexOf(original, (byte)'4')] = (byte)'3';
            }
        };

        NodeFunctionsProject captured = await CaptureAsync(files);
        files.BeforeRead = null;
        NodeFunctionsProject next = await CaptureAsync(files);

        captured.Capabilities.Should().BeEquivalentTo(before.Capabilities);
        next.Capabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_FactoryLanguageLabel_DoesNotOverrideInspectedCapabilities()
    {
        var files = new FakeFileReader();
        IReadOnlyList<ProjectCapability> capabilities = await new NodeProjectCapabilityReader(files).ReadAsync(Root, CancellationToken.None);
        var project = new NodeFunctionsProject(WorkingDirectory.FromExplicit(Root), "TypeScript", capabilities);

        project.Capabilities[0].Support.Should().Be(ProjectCapabilitySupport.Supported);
    }

    [Fact]
    public async Task Capture_PreCanceled_DoesNotInspectTheProject()
    {
        var files = new FakeFileReader();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await FluentActions.Awaiting(() => CaptureAsync(files, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        files.ReadPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_CanceledDuringRead_PropagatesTheOperationToken()
    {
        var files = new FakeFileReader();
        using var cancellation = new CancellationTokenSource();
        files.BeforeRead = (_, token) =>
        {
            token.Should().Be(cancellation.Token);
            cancellation.Cancel();
        };

        ExceptionAssertions<OperationCanceledException> error =
            await FluentActions.Awaiting(() => CaptureAsync(files, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();

        error.Which.CancellationToken.Should().Be(cancellation.Token);
        files.ReadPaths.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative-root")]
    public async Task Capture_InvalidRoot_ThrowsBeforeReading(string root)
    {
        var files = new FakeFileReader();
        var reader = new NodeProjectCapabilityReader(files);
        await FluentActions.Awaiting(() => reader.ReadAsync(root, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();

        files.ReadPaths.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_NullRoot_Throws()
    {
        var reader = new NodeProjectCapabilityReader(new FakeFileReader());

        await FluentActions.Awaiting(() => reader.ReadAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_NullDependencies_Throw()
    {
        FluentActions.Invoking(() => new NodeProjectCapabilityReader(null!)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new NodeFunctionsProject(null!, "JavaScript", [])).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new NodeFunctionsProject(WorkingDirectory.FromExplicit(Root), null!, [])).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new NodeFunctionsProject(WorkingDirectory.FromExplicit(Root), "JavaScript", null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    private static async Task<NodeFunctionsProject> CaptureAsync(FakeFileReader files, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProjectCapability> capabilities = await new NodeProjectCapabilityReader(files).ReadAsync(Root, cancellationToken);
        return new(WorkingDirectory.FromExplicit(Root), "JavaScript", capabilities);
    }

    private static void AssertSupport(NodeFunctionsProject project, ProjectCapabilitySupport? support)
    {
        if (support is null)
        {
            project.Capabilities.Should().BeEmpty();
        }
        else
        {
            project.Capabilities.Should().ContainSingle().Which.Support.Should().Be(support.Value);
        }
    }

    private sealed class FakeFileReader : NodeProjectFileReader
    {
        public Dictionary<string, (NodeProjectFileKind Kind, byte[] Bytes)> Entries { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

        public List<string> ReadPaths { get; } = [];

        public List<string> ReadRoots { get; } = [];

        public Action<string, CancellationToken>? BeforeRead { get; set; }

        public FakeFileReader()
        {
            Put("package.json", Package);
            Put("host.json", """{"version":"2.0"}""");
        }

        public void Put(string path, string contents) => Entries[path] = (NodeProjectFileKind.RegularFile, Encoding.UTF8.GetBytes(contents));

        public override Task<(NodeProjectFileKind Kind, byte[] Bytes)> ReadAsync(
            string projectRoot, string logicalPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadPaths.Add(logicalPath);
            ReadRoots.Add(projectRoot);
            BeforeRead?.Invoke(logicalPath, cancellationToken);
            if (Failures.TryGetValue(logicalPath, out Exception? exception))
            {
                throw exception;
            }

            return Task.FromResult(Entries.GetValueOrDefault(logicalPath, (NodeProjectFileKind.Absent, [])));
        }
    }
}
