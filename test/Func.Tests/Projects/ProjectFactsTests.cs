// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed partial class ProjectFactsTests
{
    private const string Authority = "example.runtime";
    private const int ShortNameBufferLength = 32768;
    private static readonly Guid _captureId = new("42db8847-ec89-44ac-b380-c41a28ebef62");
    private static readonly string _evidence = new('A', 64);

    private static string Root => Path.DirectorySeparatorChar == '\\' ? @"C:\projects\sample" : "/projects/sample";

    [Fact]
    public async Task GetProjectFactsAsync_LegacyStack_ReturnsNoFactsDespiteKnownStackIdentity()
    {
        var project = new LegacyProject();
        var context = new ProjectFactsContext(Root, project.StackName, _captureId);

        var facts = await project.GetProjectFactsAsync(context, CancellationToken.None);

        facts.Should().BeNull();
        context.Authority.Should().Be(project.StackName);
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    [InlineData(ProjectCapabilitySupport.Unknown)]
    public async Task GetProjectFactsAsync_StackOverride_PreservesReportedKnowledge(ProjectCapabilitySupport support)
    {
        var project = new FactsProject([new("future.semantic-capability", support)]);
        var context = new ProjectFactsContext(project.WorkingDirectory.Info.FullName, project.StackName, _captureId);

        var facts = await project.GetProjectFactsAsync(context, CancellationToken.None);

        facts.Should().NotBeNull();
        facts!.Context.ProjectRoot.Should().Be(Root);
        facts.Context.Authority.Should().Be(Authority);
        facts.Context.CaptureId.Should().Be(_captureId);
        facts.IsAuthoritative.Should().BeTrue();
        facts.Capabilities.Should().ContainSingle().Which.Support.Should().Be(support);
    }

    [Fact]
    public async Task GetProjectFactsAsync_MixedStackReports_SnapshotsCallerCollection()
    {
        List<ProjectCapability> capabilities =
        [
            new("supported", ProjectCapabilitySupport.Supported),
            new("unsupported", ProjectCapabilitySupport.Unsupported),
            new("unknown", ProjectCapabilitySupport.Unknown),
        ];
        var project = new FactsProject(capabilities);
        var context = new ProjectFactsContext(Root, Authority, _captureId);

        var facts = await project.GetProjectFactsAsync(context, CancellationToken.None);
        var fingerprint = facts!.Fingerprint;
        capabilities.Clear();

        facts.Capabilities.Select(capability => capability.Support).Should().BeEquivalentTo(
            [ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unsupported, ProjectCapabilitySupport.Unknown]);
        facts.Fingerprint.Should().Be(fingerprint);
    }

    [Fact]
    public async Task GetProjectFactsAsync_DefaultOperation_HonorsCancellation()
    {
        var project = new LegacyProject();
        var context = new ProjectFactsContext(Root, null, _captureId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> action = async () => await project.GetProjectFactsAsync(context, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetProjectFactsAsync_NullContext_Throws()
    {
        var project = new LegacyProject();

        Func<Task> action = async () => await project.GetProjectFactsAsync(null!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void Context_NormalizesDotSegmentsAndTrailingSeparator()
    {
        var context = new ProjectFactsContext(Path.Combine(Root, "nested", "..") + Path.DirectorySeparatorChar, Authority, _captureId);

        context.ProjectRoot.Should().Be(Root);
        context.Authority.Should().Be(Authority);
        context.CaptureId.Should().Be(_captureId);
    }

    [WindowsTheory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\projects\sample", @"C:\projects\sample")]
    [InlineData(@"C:\projects\\sample\\", @"C:\projects\sample")]
    [InlineData(@"c:/projects//sample/", @"c:\projects\sample")]
    [InlineData(@"C:\projects\.\sample", @"C:\projects\sample")]
    [InlineData(@"C:\projects\nested\..\sample\", @"C:\projects\sample")]
    [InlineData(@"C:\..\..\sample", @"C:\sample")]
    [InlineData(@"C:\projects\sample\..\..", @"C:\")]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"\\server\share\one\\.\two\..\sample\", @"\\server\share\one\sample")]
    [InlineData(@"\\server\share\..\..\sample", @"\\server\share\sample")]
    [InlineData(@"\\server\\share\\one\..\sample\", @"\\server\share\sample")]
    [InlineData(@"\\\\server\share\sample", @"\\\server\share\sample")]
    [InlineData(@"\\server\share ", @"\\server\share")]
    [InlineData(@"//server/share/one/../sample/", @"\\server\share\sample")]
    [InlineData(@"\\.\C:\", @"\\.\C:\")]
    [InlineData(@"\\.\C:\one\\.\two\..\sample\", @"\\.\C:\one\sample")]
    [InlineData(@"\\.\UNC\server\share\one\..\..\sample", @"\\.\UNC\server\sample")]
    [InlineData(@"\\.\Volume{00000000-0000-0000-0000-000000000001}\one\..\sample\", @"\\.\Volume{00000000-0000-0000-0000-000000000001}\sample")]
    [InlineData(@"\\?\C:\", @"\\?\C:\")]
    [InlineData(@"\\?\C:\one\\.\two\..\sample\", @"\\?\C:\one\\.\two\..\sample")]
    [InlineData(@"\\?\UNC\server\share\one\..\sample\", @"\\?\UNC\server\share\one\..\sample")]
    [InlineData(@"\\?\C:\one\\\", @"\\?\C:\one\\")]
    [InlineData(@"\??\C:\one\.\two\..\", @"\??\C:\one\.\two\..")]
    [InlineData(@"//?/C:/one/./two/../sample/", @"\\?\C:\one\sample")]
    [InlineData(@"//./C:/one/./two/../sample/", @"\\.\C:\one\sample")]
    [InlineData(@"C:\projects\sample... ", @"C:\projects\sample")]
    [InlineData(@"C:\projects\one. \sample ", @"C:\projects\one. \sample")]
    [InlineData(@"C:\projects\sample...\", @"C:\projects\sample...")]
    [InlineData(@"C:\projects\. ", @"C:\projects")]
    [InlineData(@"C:\projects\.. ", @"C:\projects")]
    [InlineData(@"C:\projects\...", @"C:\projects")]
    [InlineData(@"C:\projects\..\sample...", @"C:\sample")]
    [InlineData(@"\\.\C:\one\sample... ", @"\\.\C:\one\sample")]
    [InlineData(@"//?/UNC/server/share/one/../../sample/", @"\\?\UNC\server\sample")]
    public void Context_WindowsPathForms_HaveExplicitLexicalIdentities(string path, string expected)
    {
        var context = new ProjectFactsContext(path, Authority, _captureId);

        context.ProjectRoot.Should().Be(expected);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("nested", "nested")]
    [InlineData("nested/./child", "nested/child")]
    [InlineData("nested/../child", "child")]
    [InlineData("../../child", "child")]
    public void Context_OrdinaryRootedPaths_NormalizeLexically(string suffix, string expectedSuffix)
    {
        var root = Path.DirectorySeparatorChar == '\\' ? @"C:\" : "/";
        var path = root + suffix.Replace('/', Path.DirectorySeparatorChar);
        var expected = root + expectedSuffix.Replace('/', Path.DirectorySeparatorChar);

        var context = new ProjectFactsContext(path, Authority, _captureId);

        context.ProjectRoot.Should().Be(expected);
    }

    [Fact]
    public void Context_LiteralTildeSegment_DoesNotMeanHomeOrAlias()
    {
        var path = Path.Combine(Root, "~", "nested", "..", "project");
        var expected = Path.Combine(Root, "~", "project");

        var context = new ProjectFactsContext(path, Authority, _captureId);

        context.ProjectRoot.Should().Be(expected);
    }

    [Fact]
    public void Context_NonexistentComponents_DoNotRequireFilesystemExistence()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"func-project-facts-missing-{Guid.NewGuid():N}");
        Directory.Exists(missing).Should().BeFalse();
        var path = Path.Combine(missing, "MISSING~1", "~", ".", "nested", "..", "project");
        var expected = Path.Combine(missing, "MISSING~1", "~", "project");

        var context = new ProjectFactsContext(path, Authority, _captureId);

        context.ProjectRoot.Should().Be(expected);
        Directory.Exists(missing).Should().BeFalse();
    }

    [WindowsShortNameFact]
    public unsafe void Context_WindowsShortNameAlias_RemainsLexicalRegardlessOfDirectoryExistence()
    {
        var fixture = Directory.CreateTempSubdirectory("func-project-facts-");
        try
        {
            var longDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Project root with a long name"));
            var (length, error, shortPath) = CaptureShortName(longDirectory.FullName);

            length.Should().BeGreaterThan(0, $"GetShortPathNameW must succeed (Win32 error {error})");
            length.Should().BeLessThan((uint)ShortNameBufferLength);
            Path.GetFileName(shortPath).Should().NotBeEquivalentTo(longDirectory.Name, "the qualification requires an actual alias");
            shortPath.Should().Contain("~", "this regression requires a real Windows short-name alias");
            Directory.Exists(shortPath).Should().BeTrue();
            Directory.Exists(longDirectory.FullName).Should().BeTrue();

            var context = new ProjectFactsContext(shortPath, Authority, _captureId);

            context.ProjectRoot.Should().Be(shortPath);
            context.ProjectRoot.Should().NotBe(longDirectory.FullName);

            longDirectory.Delete();
            Directory.Exists(shortPath).Should().BeFalse();
            var missing = new ProjectFactsContext(shortPath, Authority, _captureId);
            missing.ProjectRoot.Should().Be(context.ProjectRoot);
        }
        finally
        {
            fixture.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(0u, 1, "", true)]
    [InlineData(0u, 50, "", true)]
    [InlineData(25u, 0, @"C:\temp\Project root with a long name", true)]
    [InlineData(25u, 0, @"C:\TEMP~1\Project root with a long name", true)]
    [InlineData(25u, 0, @"C:\temp\PROJEC~1", false)]
    [InlineData(0u, 2, "", false)]
    [InlineData(0u, 5, "", false)]
    [InlineData(0u, 87, "", false)]
    [InlineData(32768u, 0, "", false)]
    public void ShortNameQualification_OnlyUnavailablePrerequisitesAreSkipped(uint length, int error, string shortPath, bool unavailable)
    {
        var reason = GetShortNameSkipReason(length, error, shortPath.Replace('\\', Path.DirectorySeparatorChar), "Project root with a long name");

        if (unavailable)
        {
            reason.Should().StartWith("Real short-name qualification unavailable:");
        }
        else
        {
            reason.Should().BeNull("unexpected errors and malformed fixtures must execute the failing qualification, not skip it");
        }
    }

    [Fact]
    public void ShortNameFact_NonWindows_SkipsWithoutProbing()
    {
        var attribute = new WindowsShortNameFactAttribute(false, () => throw new InvalidOperationException("Must not probe Windows APIs."));

        attribute.Skip.Should().Be("Real short-name qualification unavailable: requires Windows.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Real short-name qualification unavailable: no fixture alias.")]
    public void ShortNameFact_UsesPrerequisiteResultAsXunitSkipReason(string? reason)
    {
        var attribute = new WindowsShortNameFactAttribute(true, () => reason);

        attribute.Skip.Should().Be(reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative-project")]
    public void Context_InvalidRoot_Throws(string? root)
    {
        Action action = () => new ProjectFactsContext(root!, Authority, _captureId);

        action.Should().Throw<ArgumentException>().WithParameterName("projectRoot");
    }

    [Fact]
    public void Context_NullCharacterInRoot_Throws()
    {
        Action action = () => new ProjectFactsContext(Root + "\0invalid", Authority, _captureId);

        action.Should().Throw<ArgumentException>().WithParameterName("projectRoot");
    }

    [WindowsTheory]
    [InlineData(@"C:relative")]
    [InlineData(@"\root-relative")]
    [InlineData(@"~\project")]
    public void Context_WindowsPartiallyQualifiedPaths_RemainRejected(string path)
    {
        Action action = () => new ProjectFactsContext(path, Authority, _captureId);

        action.Should().Throw<ArgumentException>().WithParameterName("projectRoot");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" example.stack")]
    [InlineData("example.stack ")]
    [InlineData("example.stack\n")]
    public void Context_InvalidAuthority_Throws(string authority)
    {
        Action action = () => new ProjectFactsContext(Root, authority, _captureId);

        action.Should().Throw<ArgumentException>().WithParameterName("authority");
    }

    [Fact]
    public void Context_EmptyCaptureId_Throws()
    {
        Action action = () => new ProjectFactsContext(Root, Authority, Guid.Empty);

        action.Should().Throw<ArgumentException>().WithParameterName("captureId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-capability")]
    [InlineData("capability name")]
    [InlineData("capability>=4")]
    [InlineData("capability/*")]
    [InlineData("\u00e9")]
    public void Capability_InvalidIdentifier_Throws(string? id)
    {
        Action action = () => new ProjectCapability(id!, ProjectCapabilitySupport.Supported);

        action.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void Capability_InvalidSupport_Throws()
    {
        Action action = () => new ProjectCapability("capability", (ProjectCapabilitySupport)100);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("support");
    }

    [Fact]
    public void Facts_CapturedCapabilities_RejectMutation()
    {
        var facts = new ProjectFacts(new(Root, Authority, _captureId), _evidence, true, [new("a", ProjectCapabilitySupport.Supported)]);

        Action action = () => ((IList<ProjectCapability>)facts.Capabilities).Clear();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Facts_ConflictingEvidence_IsPreservedRatherThanDiscarded()
    {
        var facts = new ProjectFacts(new(Root, Authority, _captureId), _evidence, true,
            [new("a", ProjectCapabilitySupport.Supported), new("a", ProjectCapabilitySupport.Unsupported)]);

        facts.Capabilities.Select(capability => capability.Support).Should().Equal(
            ProjectCapabilitySupport.Supported, ProjectCapabilitySupport.Unsupported);
    }

    [Fact]
    public void Facts_SortedSnapshot_PreservesCallerOrderAndRepeatedReports()
    {
        ProjectCapability[] capabilities =
        [
            new("b", ProjectCapabilitySupport.Supported),
            new("a", ProjectCapabilitySupport.Unsupported),
            new("a", ProjectCapabilitySupport.Supported),
            new("A", ProjectCapabilitySupport.Unknown),
            new("a", ProjectCapabilitySupport.Unknown),
            new("a", ProjectCapabilitySupport.Supported),
        ];
        var callerOrder = capabilities.ToList();

        var facts = new ProjectFacts(new(Root, Authority, _captureId), _evidence, true, capabilities);

        capabilities.Should().Equal(callerOrder);
        facts.Capabilities.Select(capability => (capability.Id, capability.Support)).Should().Equal(
            ("A", ProjectCapabilitySupport.Unknown),
            ("a", ProjectCapabilitySupport.Unknown),
            ("a", ProjectCapabilitySupport.Supported),
            ("a", ProjectCapabilitySupport.Supported),
            ("a", ProjectCapabilitySupport.Unsupported),
            ("b", ProjectCapabilitySupport.Supported));
    }

    [Fact]
    public void Facts_CapabilitySource_IsEnumeratedOnce()
    {
        var enumerations = 0;
        IEnumerable<ProjectCapability> ReadCapabilities()
        {
            enumerations++;
            if (enumerations != 1)
            {
                throw new InvalidOperationException("The capability source must be enumerated only once.");
            }

            yield return new("b", ProjectCapabilitySupport.Unsupported);
            yield return new("a", ProjectCapabilitySupport.Supported);
        }

        var facts = new ProjectFacts(new(Root, Authority, _captureId), _evidence, true, ReadCapabilities());

        enumerations.Should().Be(1);
        facts.Capabilities.Select(capability => capability.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void Facts_EquivalentReorderedInputs_HaveSameFingerprint()
    {
        var context = new ProjectFactsContext(Root, Authority, _captureId);
        var first = new ProjectFacts(context, _evidence, true,
            [new("b", ProjectCapabilitySupport.Unsupported), new("a", ProjectCapabilitySupport.Supported)]);
        var second = new ProjectFacts(context, _evidence.ToLowerInvariant(), true,
            [new("a", ProjectCapabilitySupport.Supported), new("b", ProjectCapabilitySupport.Unsupported)]);

        first.Fingerprint.Should().Be(second.Fingerprint);
        first.EvidenceFingerprint.Should().Be(_evidence);
        first.Capabilities.Select(capability => capability.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void Facts_CompleteSnapshot_PreservesCanonicalFingerprint()
    {
        var facts = new ProjectFacts(new(Root, Authority, _captureId), _evidence, true,
            [new("b", ProjectCapabilitySupport.Unsupported), new("a", ProjectCapabilitySupport.Supported)]);
        var expected = OperatingSystem.IsWindows()
            ? "FF00666DE5EA173E556314A265ACB2925D54035B9DFA7DB3BDAD833E4F2561C6"
            : "A18B16CCB8705BEF9BC762165BA1BE6963004505391BAE7D53F0BA4570FA624F";

        facts.Fingerprint.Should().Be(expected);
    }

    [Fact]
    public void Facts_ChangedBindingOrEvidence_ChangesFingerprint()
    {
        var context = new ProjectFactsContext(Root, Authority, _captureId);
        ProjectCapability[] capabilities = [new("a", ProjectCapabilitySupport.Supported)];
        var original = new ProjectFacts(context, _evidence, true, capabilities);
        ProjectFacts[] changed =
        [
            new(new(Path.Combine(Root, "other"), Authority, _captureId), _evidence, true, capabilities),
            new(new(Root, "other.owner", _captureId), _evidence, true, capabilities),
            new(new(Root, Authority, new("e5335115-88c5-44fd-a7a2-67dff33a354d")), _evidence, true, capabilities),
            new(context, new('B', 64), true, capabilities),
            new(context, _evidence, false, capabilities),
            new(context, _evidence, true, [new("a", ProjectCapabilitySupport.Unsupported)]),
            new(context, _evidence, true, [new("b", ProjectCapabilitySupport.Supported)]),
            new(context, _evidence, true, [capabilities[0], capabilities[0]]),
        ];

        changed.Should().OnlyContain(facts => facts.Fingerprint != original.Fingerprint);
        original.Fingerprint.Should().HaveLength(64);
    }

    [Theory]
    [InlineData("")]
    [InlineData("stale")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Facts_InvalidEvidenceFingerprint_Throws(string evidence)
    {
        Action action = () => new ProjectFacts(new(Root, Authority, _captureId), evidence, true, []);

        action.Should().Throw<ArgumentException>().WithParameterName("evidenceFingerprint");
    }

    [Fact]
    public void Facts_NullInputs_Throw()
    {
        var context = new ProjectFactsContext(Root, Authority, _captureId);
        Action nullContext = () => new ProjectFacts(null!, _evidence, true, []);
        Action nullEvidence = () => new ProjectFacts(context, null!, true, []);
        Action nullCapabilities = () => new ProjectFacts(context, _evidence, true, null!);
        Action nullEntry = () => new ProjectFacts(context, _evidence, true, [null!]);

        nullContext.Should().Throw<ArgumentNullException>().WithParameterName("context");
        nullEvidence.Should().Throw<ArgumentNullException>().WithParameterName("evidenceFingerprint");
        nullCapabilities.Should().Throw<ArgumentNullException>().WithParameterName("capabilities");
        nullEntry.Should().Throw<ArgumentException>().WithParameterName("capabilities");
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static unsafe partial uint GetShortPathName(string longPath, char* shortPath, uint bufferLength);

    private static unsafe (uint Length, int Error, string ShortPath) CaptureShortName(string longPath)
    {
        var buffer = new char[ShortNameBufferLength];
        uint length;
        fixed (char* pointer = buffer)
        {
            length = GetShortPathName(longPath, pointer, (uint)buffer.Length);
        }

        int error = Marshal.GetLastPInvokeError();
        string shortPath = length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : string.Empty;
        return (length, error, shortPath);
    }

    private static string? GetShortNameSkipReason(uint length, int error, string shortPath, string longDirectoryName)
    {
        if (length == 0 && error is 1 or 50)
        {
            return $"Real short-name qualification unavailable: GetShortPathNameW is not supported (Win32 error {error}).";
        }

        if (length > 0 && length < ShortNameBufferLength
            && string.Equals(Path.GetFileName(shortPath), longDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return "Real short-name qualification unavailable: the temporary filesystem did not provide an 8.3 alias for the fixture directory.";
        }

        // Leave unexpected errors and malformed native results unskipped so the qualification reports a failure.
        return null;
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "Lexical Windows path cases require Windows.")]
    public void WindowsTheory_Prerequisite_IsReportedToXunit(bool isWindows, string? expectedSkip)
    {
        var attribute = new WindowsTheoryAttribute(isWindows);

        attribute.Skip.Should().Be(expectedSkip);
    }

    private sealed class WindowsTheoryAttribute : TheoryAttribute
    {
        public WindowsTheoryAttribute()
            : this(OperatingSystem.IsWindows())
        {
        }

        internal WindowsTheoryAttribute(bool isWindows)
        {
            Skip = isWindows ? null : "Lexical Windows path cases require Windows.";
        }
    }

    private sealed class WindowsShortNameFactAttribute : FactAttribute
    {
        public WindowsShortNameFactAttribute()
            : this(OperatingSystem.IsWindows(), ProbePrerequisite)
        {
        }

        internal WindowsShortNameFactAttribute(bool isWindows, Func<string?> probe)
        {
            ArgumentNullException.ThrowIfNull(probe);
            Skip = isWindows ? probe() : "Real short-name qualification unavailable: requires Windows.";
        }

        private static string? ProbePrerequisite()
        {
            var fixture = Directory.CreateTempSubdirectory("func-project-facts-");
            try
            {
                var longDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, "Project root with a long name"));
                var (length, error, shortPath) = CaptureShortName(longDirectory.FullName);
                return GetShortNameSkipReason(length, error, shortPath, longDirectory.Name);
            }
            finally
            {
                fixture.Delete(recursive: true);
            }
        }
    }

    private class LegacyProject : FunctionsProject
    {
        public override WorkingDirectory WorkingDirectory => WorkingDirectory.FromExplicit(Root);

        public override string StackName => Authority;

        public override string StackDisplayName => throw new InvalidOperationException("Facts must not read display names.");

        public override bool SupportsExtensionBundles => throw new InvalidOperationException("Facts must not infer bundle support.");

        public override FunctionsWorkerReference WorkerReference => throw new InvalidOperationException("Facts must not inspect worker selection.");
    }

    private sealed class FactsProject(IEnumerable<ProjectCapability> capabilities) : LegacyProject
    {
        public override ValueTask<ProjectFacts?> GetProjectFactsAsync(ProjectFactsContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<ProjectFacts?>(new(context, _evidence, true, capabilities));
        }
    }
}
