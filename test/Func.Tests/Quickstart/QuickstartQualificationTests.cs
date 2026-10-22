// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Quickstart;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Quickstart;

public sealed class QuickstartQualificationTests
{
    [Theory]
    [InlineData("catalog")]
    [InlineData("planner")]
    [InlineData("templates")]
    [InlineData("workloads")]
    [InlineData("hostVersion")]
    public void Constructor_NullDependency_RejectsProgrammerError(string dependency)
    {
        Action construct = () => _ = new QuickstartQualification(
            dependency == "catalog" ? null! : Substitute.For<IBasicCompanionCatalog>(),
            dependency == "planner" ? null! : new(),
            dependency == "templates" ? null! : Substitute.For<IBasicTemplateLifecycle>(),
            dependency == "workloads" ? null! : Substitute.For<IWorkloadStore>(),
            dependency == "hostVersion" ? null! : Substitute.For<ICliVersionProvider>());

        construct.Should().Throw<ArgumentNullException>().WithParameterName(dependency);
    }

    [Fact]
    public async Task ResolveAsync_NullNomination_RejectsBeforeReadingAuthority()
    {
        var catalog = Substitute.For<IBasicCompanionCatalog>();
        QuickstartQualification service = new(catalog, new(), Substitute.For<IBasicTemplateLifecycle>(),
            Substitute.For<IWorkloadStore>(), Substitute.For<ICliVersionProvider>());

        Func<Task> resolve = () => service.ResolveAsync(null!, new(WorkingDirectory.FromExplicit(Path.GetTempPath()),
            new("Owner", "1.0.0", "https://fixture.invalid")), new() { PackageId = "Owner", PackageVersion = "1.0.0" }, CancellationToken.None);

        await resolve.Should().ThrowAsync<ArgumentNullException>().WithParameterName("nomination");
        catalog.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Approval_NullPlanOrArtifacts_RejectsProgrammerError()
    {
        Action plan = () => _ = new BasicAcquisitionApproval(null!, []);
        Action artifacts = () => _ = new BasicAcquisitionApproval(new("fixture", "https://fixture.invalid", []), null!);

        plan.Should().Throw<ArgumentNullException>().WithParameterName("Plan");
        artifacts.Should().Throw<ArgumentNullException>().WithParameterName("Artifacts");
    }
}
