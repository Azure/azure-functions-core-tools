// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Tests.Templates.Preparation;

public sealed class PreparedOperationPlanTests
{
    public static IEnumerable<object[]> DeviceNamespaceTargets()
    {
        foreach (char first in @"\/")
        {
            foreach (char second in @"\/")
            {
                foreach (char last in @"\/")
                {
                    yield return [$"{first}{second}?{last}C:{last}project"];
                    yield return [$"{first}{second}.{last}C:{last}project"];
                }

                yield return [$"{first}??{second}C:{second}project"];
            }
        }

        yield return [@"\\?\UNC\server\share\project"];
        yield return ["//?/UNC/server/share/project"];
        yield return [@"\/.\UNC/server\share/project"];
    }

    [Fact]
    public void Constructor_CallerChangesBytesAndEntries_RetainsIndependentOrderedSnapshots()
    {
        byte[] bytes = [0, 255, 13, 10];
        PreparedOutputEntry first = new(@"src\handler.bin", PreparedOutputKind.File, bytes);
        List<PreparedOutputEntry> entries = [first, new("empty", PreparedOutputKind.Directory), new("a.txt", PreparedOutputKind.File)];

        PreparedOperationPlan plan = new(PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity, entries);
        bytes[0] = 42;
        entries.Clear();

        plan.Entries.Select(entry => entry.RelativePath).Should().Equal("a.txt", "empty", "src/handler.bin");
        first.Content.Should().Equal(0, 255, 13, 10);
        ((IList<PreparedOutputEntry>)plan.Entries).Invoking(list => list.Clear()).Should().Throw<NotSupportedException>();
        ((IList<byte>)first.Content).Invoking(list => list[0] = 9).Should().Throw<NotSupportedException>();
        plan.RequiredDirectories.Should().Contain(directory =>
            directory.Path == PreparedOperationTestFileSystem.Destination("empty") && directory.MustBeNew);
        plan.RequiredDirectories.Should().Contain(directory =>
            directory.Path == PreparedOperationTestFileSystem.Destination("src") && !directory.MustBeNew);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("src/../escape")]
    [InlineData("src/./file")]
    [InlineData("/rooted")]
    [InlineData("\\rooted")]
    [InlineData("C:\\rooted")]
    [InlineData("\\\\server\\share\\file")]
    [InlineData("src//file")]
    [InlineData("src/")]
    [InlineData("file:stream")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("src/\0file")]
    [InlineData("src/\nfile")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("CON .txt")]
    [InlineData("LPT1.txt")]
    [InlineData("COM\u00b9")]
    [InlineData("wild*card")]
    public void Constructor_UnsafeLogicalPath_RejectsBeforePublication(string path)
    {
        Action action = () => _ = new PreparedOutputEntry(path, PreparedOutputKind.File);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_InvalidEntryContracts_Rejects()
    {
        Action nullPath = () => _ = new PreparedOutputEntry(null!, PreparedOutputKind.File);
        Action invalidKind = () => _ = new PreparedOutputEntry("file", (PreparedOutputKind)99);
        Action directoryBytes = () => _ = new PreparedOutputEntry("directory", PreparedOutputKind.Directory, [1]);

        nullPath.Should().Throw<ArgumentNullException>();
        invalidKind.Should().Throw<ArgumentOutOfRangeException>();
        directoryBytes.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("dir/file.txt", "dir\\file.txt")]
    [InlineData("File.txt", "file.txt")]
    [InlineData("directory", "directory/file.txt")]
    [InlineData("Dir/a.txt", "dir/b.txt")]
    public void Constructor_DuplicateAncestorOrCaseAlias_Rejects(string first, string second)
    {
        Action action = () => PreparedOperationTestFileSystem.Plan(
            new(first, PreparedOutputKind.File), new(second, PreparedOutputKind.File));

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_FileAndDirectoryAtSameDestination_Rejects()
    {
        Action action = () => PreparedOperationTestFileSystem.Plan(
            new("output", PreparedOutputKind.Directory), new("output", PreparedOutputKind.File));

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(".func")]
    [InlineData(".func/config.json")]
    [InlineData("nested/.FUNC/config.json")]
    public void Constructor_TemplateOccupiesConfigurationNamespace_Rejects(string path)
    {
        Action action = () => PreparedOperationTestFileSystem.Plan(new(path, PreparedOutputKind.File));

        action.Should().Throw<ArgumentException>().WithMessage("*CLI-owned*");
    }

    [Fact]
    public void Constructor_PreSerializedConfiguration_RetainsBytesAndReservedLocation()
    {
        byte[] bytes = [123, 13, 10, 125];
        PreparedConfiguration configuration = new("project", bytes);
        PreparedOperationPlan plan = new(
            PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity,
            [new("project/host.json", PreparedOutputKind.File, [123, 125])], configuration);

        bytes[0] = 0;

        plan.Configuration.Should().BeSameAs(configuration);
        configuration.Entry.RelativePath.Should().Be("project/.func/config.json");
        configuration.Entry.Content.Should().Equal(123, 13, 10, 125);
        plan.FilesToCommit.Last().Should().BeSameAs(configuration.Entry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_ConfigurationHasNoRegularHostAtItsRoot_Rejects(bool hostIsDirectory)
    {
        PreparedOutputEntry entry = hostIsDirectory
            ? new("host.json", PreparedOutputKind.Directory)
            : new("other/host.json", PreparedOutputKind.File);
        Action action = () => _ = new PreparedOperationPlan(
            PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity, [entry], new(".", [123, 125]));

        action.Should().Throw<ArgumentException>().WithMessage("*regular host.json*");
    }

    [Fact]
    public void Constructor_ConfigurationInvalidContracts_Rejects()
    {
        Action empty = () => _ = new PreparedConfiguration(".", []);
        Action nullRoot = () => _ = new PreparedConfiguration(null!, [1]);
        Action escape = () => _ = new PreparedConfiguration("../project", [1]);

        empty.Should().Throw<ArgumentException>();
        nullRoot.Should().Throw<ArgumentNullException>();
        escape.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_CompatibleProject_RetainsTheExactResult()
    {
        ItemTemplateCompatibilityResult compatibility = Compatibility(ItemTemplateCompatibility.Compatible);

        PreparedOperationPlan plan = new(
            PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity,
            [new("item.txt", PreparedOutputKind.File)], compatibility: compatibility);

        plan.Compatibility.Should().BeSameAs(compatibility);
        plan.Compatibility!.Project.Should().BeSameAs(compatibility.Project);
        plan.Configuration.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_IncompatibleOrUnrelatedProject_Rejects(bool unrelatedRoot)
    {
        ItemTemplateCompatibilityResult compatibility = Compatibility(
            unrelatedRoot ? ItemTemplateCompatibility.Compatible : ItemTemplateCompatibility.Incompatible,
            unrelatedRoot ? PreparedOperationTestFileSystem.Destination("other") : null);
        Action action = () => _ = new PreparedOperationPlan(
            PreparedOperationTestFileSystem.Target, PreparedOperationTestFileSystem.Identity,
            [new("item.txt", PreparedOutputKind.File)], compatibility: compatibility);

        action.Should().Throw<ArgumentException>().WithMessage("*Compatible*exact target*");
    }

    [Fact]
    public void Constructor_MissingIdentityEntriesOrAbsoluteTarget_Rejects()
    {
        PreparedOperationIdentity identity = PreparedOperationTestFileSystem.Identity;
        PreparedOutputEntry[] entries = [new("item.txt", PreparedOutputKind.File)];
        Action nullTarget = () => _ = new PreparedOperationPlan(null!, identity, entries);
        Action relativeTarget = () => _ = new PreparedOperationPlan("relative", identity, entries);
        Action deviceTarget = () => _ = new PreparedOperationPlan(@"\\?\C:\project", identity, entries);
        Action nullIdentity = () => _ = new PreparedOperationPlan(PreparedOperationTestFileSystem.Target, null!, entries);
        Action nullEntries = () => _ = new PreparedOperationPlan(PreparedOperationTestFileSystem.Target, identity, null!);
        Action emptyEntries = () => _ = new PreparedOperationPlan(PreparedOperationTestFileSystem.Target, identity, []);
        Action nullEntry = () => _ = new PreparedOperationPlan(PreparedOperationTestFileSystem.Target, identity, [null!]);

        nullTarget.Should().Throw<ArgumentNullException>();
        relativeTarget.Should().Throw<ArgumentException>();
        deviceTarget.Should().Throw<ArgumentException>();
        nullIdentity.Should().Throw<ArgumentNullException>();
        nullEntries.Should().Throw<ArgumentNullException>();
        emptyEntries.Should().Throw<ArgumentException>();
        nullEntry.Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(DeviceNamespaceTargets))]
    public void Constructor_DeviceNamespaceWithAnyPrefixSeparatorSpelling_Rejects(string target)
    {
        Action construct = () => _ = new PreparedOperationPlan(
            target, PreparedOperationTestFileSystem.Identity, [new("item", PreparedOutputKind.File)]);

        construct.Should().Throw<ArgumentException>().WithMessage("*not a device namespace*");
    }

    [Theory]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\projects\nested\..\sample\", @"C:\projects\sample")]
    [InlineData(@"c:/projects//./sample/", @"c:\projects\sample")]
    [InlineData(@"C:\..\..\sample", @"C:\sample")]
    [InlineData(@"C:\projects\sample... ", @"C:\projects\sample")]
    [InlineData(@"C:\projects\one. \sample ", @"C:\projects\one. \sample")]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    [InlineData(@"//server/share/one/../sample/", @"\\server\share\sample")]
    [InlineData(@"\\server\share\..\..\sample", @"\\server\share\sample")]
    [InlineData(@"C:\SHORT~1\project", @"C:\SHORT~1\project")]
    public void Constructor_OrdinaryWindowsTarget_NormalizesLexicallyAndBindsTheProject(string target, string expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        ItemTemplateCompatibilityResult compatibility = Compatibility(ItemTemplateCompatibility.Compatible, target);

        PreparedOperationPlan plan = new(target, PreparedOperationTestFileSystem.Identity,
            [new("item", PreparedOutputKind.File)], compatibility: compatibility);

        plan.TargetDirectory.Should().Be(expected);
        plan.Compatibility.Should().BeSameAs(compatibility);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("nested/./child", "nested/child")]
    [InlineData("nested/../child", "child")]
    [InlineData("../../child", "child")]
    [InlineData("SHORT~1/~/nested/../child", "SHORT~1/~/child")]
    public void Constructor_OrdinaryNativeTarget_NormalizesLexicallyAndBindsTheProject(string suffix, string expectedSuffix)
    {
        string root = Path.DirectorySeparatorChar == '\\' ? @"C:\" : "/";
        string target = root + suffix.Replace('/', Path.DirectorySeparatorChar);
        string expected = root + expectedSuffix.Replace('/', Path.DirectorySeparatorChar);
        ItemTemplateCompatibilityResult compatibility = Compatibility(ItemTemplateCompatibility.Compatible, target);

        PreparedOperationPlan plan = new(target, PreparedOperationTestFileSystem.Identity,
            [new("item", PreparedOutputKind.File)], compatibility: compatibility);

        plan.TargetDirectory.Should().Be(expected);
        plan.Compatibility.Should().BeSameAs(compatibility);
    }

    [Theory]
    [InlineData("", "1.0.0", "template")]
    [InlineData("package", "", "template")]
    [InlineData("package", "1.0.0", " ")]
    public void Constructor_BlankDiagnosticIdentity_Rejects(string packageId, string version, string template)
    {
        Action action = () => _ = new PreparedOperationIdentity(packageId, version, template);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullDiagnosticIdentifiers_Rejects()
    {
        Action package = () => _ = new PreparedOperationIdentity(null!, "1.0.0", "template");
        Action version = () => _ = new PreparedOperationIdentity("package", null!, "template");
        Action template = () => _ = new PreparedOperationIdentity("package", "1.0.0", null!);

        package.Should().Throw<ArgumentNullException>();
        version.Should().Throw<ArgumentNullException>();
        template.Should().Throw<ArgumentNullException>();
    }

    private static ItemTemplateCompatibilityResult Compatibility(ItemTemplateCompatibility compatibility, string? root = null)
        => new(new TestProject(root ?? PreparedOperationTestFileSystem.Target), new(["example.http"]), compatibility, []);

    private sealed class TestProject(string root) : FunctionsProject([new("example.http", ProjectCapabilitySupport.Supported)])
    {
        public override WorkingDirectory WorkingDirectory { get; } = new(new DirectoryInfo(root), WasExplicit: true);

        public override string StackName => "example";

        public override string StackDisplayName => "Example";

        public override bool SupportsExtensionBundles => false;

        public override FunctionsWorkerReference WorkerReference => FunctionsWorkerReference.FromWorkload("example");
    }
}
