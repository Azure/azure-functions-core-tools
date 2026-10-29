// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Integration;
using Azure.Functions.Cli.Templates.PackageIntake;
using Azure.Functions.Cli.Templates.Preparation;
using Azure.Functions.Cli.Templates.Selection;
using Azure.Functions.Cli.Tests.Templates.PackageIntake;
using Azure.Functions.Cli.Tests.Templates.Preparation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Integration;

public sealed class PackageTemplateBoundaryTests
{
    [Theory]
    [InlineData("package")]
    [InlineData("selection")]
    [InlineData("root")]
    public void Request_InvalidContractInput_IsRejected(string input)
    {
        var fixture = new PackageTemplateFixture();
        var valid = fixture.Request();

        var action = () => new PackageTemplateRequest(
            input == "package" ? null! : valid.Package,
            input == "selection" ? null! : valid.Selection,
            input == "root" ? "relative-path" : valid.TargetDirectory);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("intake")]
    [InlineData("adapter")]
    [InlineData("selector")]
    [InlineData("resolver")]
    [InlineData("matcher")]
    [InlineData("filesystem")]
    public void Application_NullDependency_IsRejected(string input)
    {
        var fixture = new PackageTemplateFixture();

        var action = () => new PackageTemplateApplication(
            input == "intake" ? null! : new(fixture.Storage.FileSystem, TemplatePackageTestData.Limits()),
            input == "adapter" ? null! : fixture.Adapter,
            input == "selector" ? null! : new(new()),
            input == "resolver" ? null! : fixture.ProjectResolver,
            input == "matcher" ? null! : new(),
            input == "filesystem" ? null! : fixture.Storage.FileSystem);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task PrepareAsync_NullRequest_IsRejectedWithoutIntake()
    {
        var fixture = new PackageTemplateFixture();

        var action = () => fixture.Application.PrepareAsync(null!, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentNullException>();
        fixture.Storage.FileSystem.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void Session_SameDescriptorBoundTwice_IsRejectedRatherThanReboundByOrder()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();

        var action = () => new FixtureSession([candidate, candidate]);

        action.Should().Throw<InvalidTemplateMetadataException>();
    }

    [Fact]
    public async Task RenderAsync_EqualIdentityButDifferentDescriptor_DoesNotSubstituteNativeCandidate()
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate();
        var other = fixture.Candidate();
        var arguments = new TemplateArgumentParser().Parse(other.Descriptor, [], []);

        var action = () => candidate.RenderAsync(arguments, null, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        candidate.RenderCalls.Should().Be(0);
    }

    [Fact]
    public void AddPackageTemplateApplication_WithoutAdapter_DoesNotInstallAProductionStubOrCommand()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IFunctionsProjectResolver>());

        services.AddPackageTemplateApplication(TemplatePackageTestData.Limits());

        services.Should().NotContain(service => service.ServiceType == typeof(IPackageTemplateAdapter));
        services.Should().NotContain(service => service.ServiceType == typeof(FuncCliCommand));
        using var provider = services.BuildServiceProvider();
        var action = () => provider.GetRequiredService<PackageTemplateApplication>();
        action.Should().Throw<InvalidOperationException>().WithMessage("*IPackageTemplateAdapter*");
    }

    [Fact]
    public void AddPackageTemplateApplication_CallerSuppliesFixtureBoundaries_ResolvesSharedApplication()
    {
        var fixture = new PackageTemplateFixture();
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Adapter);
        services.AddSingleton(fixture.ProjectResolver);
        services.AddSingleton<IFileSystem>(fixture.Storage.FileSystem);
        services.AddPackageTemplateApplication(TemplatePackageTestData.Limits());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var application = provider.GetRequiredService<PackageTemplateApplication>();

        application.Should().NotBeNull();
        provider.GetRequiredService<TemplateSelector>().Should().NotBeNull();
        provider.GetRequiredService<ItemTemplateCompatibilityMatcher>().Should().NotBeNull();
    }

    [Fact]
    public void Operation_NullPreparedOperation_IsRejected()
    {
        var action = () => new PackageTemplateOperation(null!, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Operation_NullPackage_IsRejected()
    {
        var fixture = new PackageTemplateFixture();
        var intake = new LocalTemplatePackageIntake(fixture.Storage.FileSystem, TemplatePackageTestData.Limits());
        using var package = await intake.IntakeAsync(fixture.Package, CancellationToken.None);
        var plan = PreparedOperationTestFileSystem.Plan(new("fixture.txt", PreparedOutputKind.File, "fixture"u8));
        await using var prepared = new PreparedOperation(plan, fixture.Storage.FileSystem);

        var action = () => new PackageTemplateOperation(prepared, null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Capture_NullEntries_IsRejected()
    {
        var action = () => new PackageTemplateCapture(null!);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PreparationFailure_InvalidCategory_IsRejected()
    {
        var action = () => new TemplatePreparationException((TemplatePreparationFailure)int.MaxValue, "Fixture rejection.");

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("candidates")]
    [InlineData("candidate")]
    public void Session_NullContractInput_IsRejected(string input)
    {
        var action = () => new FixtureSession(input == "candidates" ? null! : [null!]);

        if (input == "candidates")
        {
            action.Should().Throw<ArgumentNullException>();
        }
        else
        {
            action.Should().Throw<InvalidTemplateMetadataException>();
        }
    }

    [Theory]
    [InlineData("descriptor")]
    [InlineData("eligibility")]
    public void Candidate_InvalidContractInput_IsRejected(string input)
    {
        var fixture = new PackageTemplateFixture();
        var descriptor = fixture.Candidate().Descriptor;

        var action = () => new FixtureCandidate(input == "descriptor" ? null! : descriptor,
            input == "eligibility" ? (FuncTemplateConstraintOutcome)int.MaxValue : FuncTemplateConstraintOutcome.Eligible);

        action.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("incomplete")]
    public async Task RenderAsync_InvalidArguments_DoesNotRender(string input)
    {
        var fixture = new PackageTemplateFixture();
        var candidate = fixture.Candidate(parameters:
            [new("Required", "Required", null, null, "string", [], null, true, false, false)]);
        var arguments = input == "null" ? null! : new TemplateArgumentParser().Parse(candidate.Descriptor, [], []);

        var action = () => candidate.RenderAsync(arguments, null, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        candidate.RenderCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("services")]
    [InlineData("limits")]
    public void AddPackageTemplateApplication_NullContractInput_IsRejected(string input)
    {
        var action = () => PackageTemplateServiceCollectionExtensions.AddPackageTemplateApplication(
            input == "services" ? null! : new ServiceCollection(), input == "limits" ? null! : TemplatePackageTestData.Limits());

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CommitAsync_AfterDisposal_DoesNotResolveOrPublish()
    {
        var fixture = new PackageTemplateFixture();
        fixture.SetCandidates(fixture.Candidate(TemplateType.Item));
        var operation = await fixture.Application.PrepareAsync(fixture.Request(TemplateType.Item), CancellationToken.None);
        await operation.DisposeAsync();

        var action = () => operation.CommitAsync(CancellationToken.None);

        await action.Should().ThrowAsync<ObjectDisposedException>();
        await fixture.ProjectResolver.Received(1).ResolveProjectAsync(Arg.Any<ProjectResolutionContext>(), Arg.Any<CancellationToken>());
        fixture.Storage.Files.Should().BeEmpty();
        operation.Completion.Status.Should().Be(PreparedCommitStatus.NotStarted);
    }
}
