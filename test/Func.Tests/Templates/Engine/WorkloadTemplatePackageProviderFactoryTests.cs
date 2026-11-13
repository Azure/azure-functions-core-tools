// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public sealed class WorkloadTemplatePackageProviderFactoryTests
{
    [Fact]
    public async Task GetAllTemplatePackagesAsync_MapsRoots()
    {
        var source = Substitute.For<IWorkloadTemplateSource>();
        WorkloadTemplateRoot[] roots =
        [
            new("first-root", "first.workload", "1.0.0", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new("second-root", "second.workload", "2.0.0", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
        ];
        source.GetRootsAsync(default).Returns(Task.FromResult<IReadOnlyList<WorkloadTemplateRoot>>(roots));
        WorkloadTemplatePackageProviderFactory factory = new(source);
        ITemplatePackageProvider provider = factory.CreateProvider(Substitute.For<IEngineEnvironmentSettings>());

        IReadOnlyList<ITemplatePackage> packages = await provider.GetAllTemplatePackagesAsync(default);

        provider.Factory.Should().BeSameAs(factory);
        packages.Select(package => package.MountPointUri).Should().Equal(roots.Select(root => root.Path));
        packages.Select(package => package.LastChangeTime).Should().Equal(roots.Select(root => root.LastChangeTime));
        packages.Should().AllSatisfy(package => package.Provider.Should().BeSameAs(provider));
        await source.Received(1).GetRootsAsync();
    }

    [Fact]
    public async Task GetAllTemplatePackagesAsync_EmptySource_ReturnsNoPackages()
    {
        WorkloadTemplatePackageProviderFactory factory = new(new WorkloadTemplateSource());
        ITemplatePackageProvider provider = factory.CreateProvider(Substitute.For<IEngineEnvironmentSettings>());

        IReadOnlyList<ITemplatePackage> packages = await provider.GetAllTemplatePackagesAsync(CancellationToken.None);

        packages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllTemplatePackagesAsync_CanceledToken_DoesNotQuerySource()
    {
        var source = Substitute.For<IWorkloadTemplateSource>();
        WorkloadTemplatePackageProviderFactory factory = new(source);
        ITemplatePackageProvider provider = factory.CreateProvider(Substitute.For<IEngineEnvironmentSettings>());
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Func<Task> act = () => provider.GetAllTemplatePackagesAsync(cancellation.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>())
            .Which.CancellationToken.Should().Be(cancellation.Token);
        await source.DidNotReceive().GetRootsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllTemplatePackagesAsync_SourceFails_PropagatesFailure()
    {
        var source = Substitute.For<IWorkloadTemplateSource>();
        InvalidOperationException failure = new("Cannot read workload inventory.");
        source.GetRootsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<WorkloadTemplateRoot>>(failure));
        WorkloadTemplatePackageProviderFactory factory = new(source);
        ITemplatePackageProvider provider = factory.CreateProvider(Substitute.For<IEngineEnvironmentSettings>());

        Func<Task> act = () => provider.GetAllTemplatePackagesAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        Action act = () => _ = new WorkloadTemplatePackageProviderFactory(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("source");
    }

    [Fact]
    public void CreateProvider_NullSettings_Throws()
    {
        WorkloadTemplatePackageProviderFactory factory = new(new WorkloadTemplateSource());

        Action act = () => factory.CreateProvider(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }
}
