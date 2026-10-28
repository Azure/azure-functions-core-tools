// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Tests.Projects;

public sealed class FunctionsProjectTests
{
    [Fact]
    public void Constructor_ExistingStackWithoutReports_HasNoCapabilities()
    {
        var project = new TestProject();

        project.StackName.Should().Be("example.stack");
        project.Capabilities.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ProjectCapabilitySupport.Supported)]
    [InlineData(ProjectCapabilitySupport.Unsupported)]
    [InlineData(ProjectCapabilitySupport.Unknown)]
    public void Constructor_StackReports_PreservesCapabilityState(ProjectCapabilitySupport support)
    {
        var project = new TestProject([new("future.semantic-capability", support)]);

        project.Capabilities.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new ProjectCapability("future.semantic-capability", support));
    }

    [Fact]
    public void Constructor_CallerMutation_DoesNotChangeCapturedCapabilities()
    {
        List<ProjectCapability> capabilities =
        [
            new("supported", ProjectCapabilitySupport.Supported),
            new("unsupported", ProjectCapabilitySupport.Unsupported),
            new("unknown", ProjectCapabilitySupport.Unknown),
        ];
        var project = new TestProject(capabilities);
        var captured = project.Capabilities;

        capabilities.Clear();
        capabilities.Add(new("different", ProjectCapabilitySupport.Unsupported));

        project.Capabilities.Should().BeSameAs(captured);
        project.Capabilities.Select(capability => (capability.Id, capability.Support)).Should().Equal(
            ("supported", ProjectCapabilitySupport.Supported),
            ("unknown", ProjectCapabilitySupport.Unknown),
            ("unsupported", ProjectCapabilitySupport.Unsupported));
    }

    [Fact]
    public void Capabilities_CapturedCollection_RejectsMutation()
    {
        var project = new TestProject([new("a", ProjectCapabilitySupport.Supported)]);

        Action action = () => ((IList<ProjectCapability>)project.Capabilities).Clear();

        action.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Constructor_SortsCopy_PreservesCallerOrderAndRepeatedReports()
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

        var project = new TestProject(capabilities);

        capabilities.Should().Equal(callerOrder);
        project.Capabilities.Select(capability => (capability.Id, capability.Support)).Should().Equal(
            ("A", ProjectCapabilitySupport.Unknown),
            ("a", ProjectCapabilitySupport.Unknown),
            ("a", ProjectCapabilitySupport.Supported),
            ("a", ProjectCapabilitySupport.Supported),
            ("a", ProjectCapabilitySupport.Unsupported),
            ("b", ProjectCapabilitySupport.Supported));
    }

    [Fact]
    public void Constructor_CapabilitySource_IsEnumeratedOnceBeforeReportsAreRead()
    {
        var enumerations = 0;
        IEnumerable<ProjectCapability> InspectProject()
        {
            enumerations++;
            if (enumerations != 1)
            {
                throw new InvalidOperationException("The captured project must not be inspected again.");
            }

            yield return new("b", ProjectCapabilitySupport.Unsupported);
            yield return new("a", ProjectCapabilitySupport.Supported);
        }

        var project = new TestProject(InspectProject());

        enumerations.Should().Be(1);
        project.Capabilities.Select(capability => capability.Id).Should().Equal("a", "b");
        project.Capabilities.Select(capability => capability.Id).Should().Equal("a", "b");
        enumerations.Should().Be(1);
    }

    [Fact]
    public void Constructor_NewObservation_DoesNotRefreshOriginalProject()
    {
        ProjectCapability[] inputs = [new("a", ProjectCapabilitySupport.Supported)];
        var original = new TestProject(inputs);

        inputs[0] = new("a", ProjectCapabilitySupport.Unsupported);
        var observedAgain = new TestProject(inputs);

        observedAgain.Should().NotBeSameAs(original);
        observedAgain.WorkingDirectory.Info.FullName.Should().Be(original.WorkingDirectory.Info.FullName);
        observedAgain.StackName.Should().Be(original.StackName);
        original.Capabilities.Should().ContainSingle().Which.Support.Should().Be(ProjectCapabilitySupport.Supported);
        observedAgain.Capabilities.Should().ContainSingle().Which.Support.Should().Be(ProjectCapabilitySupport.Unsupported);
    }

    [Fact]
    public void Constructor_CapabilityInspectionFails_PropagatesFailure()
    {
        IEnumerable<ProjectCapability> InspectProject()
        {
            yield return new("a", ProjectCapabilitySupport.Supported);
            throw new IOException("Project inputs are unreadable.");
        }

        Action action = () => new TestProject(InspectProject());

        action.Should().Throw<IOException>().WithMessage("Project inputs are unreadable.");
    }

    [Fact]
    public void Constructor_NullCapabilities_Throws()
    {
        Action action = () => new TestProject(null!);

        action.Should().Throw<ArgumentNullException>().WithParameterName("capabilities");
    }

    [Fact]
    public void Constructor_NullCapabilityEntry_Throws()
    {
        Action action = () => new TestProject([null!]);

        action.Should().Throw<ArgumentException>().WithParameterName("capabilities");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-capability")]
    [InlineData("capability name")]
    [InlineData("capability>=4")]
    [InlineData("capability/*")]
    [InlineData("capability\n")]
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

    private sealed class TestProject : FunctionsProject
    {
        public TestProject()
        {
        }

        public TestProject(IEnumerable<ProjectCapability> capabilities)
            : base(capabilities)
        {
        }

        public override WorkingDirectory WorkingDirectory { get; } =
            WorkingDirectory.FromExplicit(OperatingSystem.IsWindows() ? @"C:\projects\sample" : "/projects/sample");

        public override string StackName => "example.stack";

        public override string StackDisplayName => throw new InvalidOperationException("Capability capture must not read display names.");

        public override bool SupportsExtensionBundles => throw new InvalidOperationException("Capability capture must not infer bundle support.");

        public override FunctionsWorkerReference WorkerReference => throw new InvalidOperationException("Capability capture must not select workers.");
    }
}
