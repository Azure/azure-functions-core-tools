// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Templates.Engine;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Templates.Acquisition;

public sealed class BasicAcquisitionContractTests
{
    [Fact]
    public void Constructors_NullDependencies_RejectProgrammerErrors()
    {
        using var fixture = new BasicAcquisitionFixture();
        var fileSystem = new PhysicalFileSystem();
        var inspector = new BasicArtifactInspector(fileSystem);
        Action[] constructors =
        [
            () => new BasicArtifactInspector(null!),
            () => new BasicPackagePayloadSource(null!),
            () => new BundledBasicCompanionCatalog(null!),
            () => new BasicTemplateLifecycle(null!, fixture.Payload, inspector, fixture.Ownership, fileSystem),
            () => new BasicTemplateLifecycle(fixture.Templates, null!, inspector, fixture.Ownership, fileSystem),
            () => new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, null!, fixture.Ownership, fileSystem),
            () => new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, inspector, null!, fileSystem),
            () => new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, inspector, fixture.Ownership, null!),
            () => new BasicAcquisitionRunner(null!, new(), fixture.SourceProvider, fixture.Store, fixture.Paths, fixture.Installer,
                Substitute.For<IBasicTemplateLifecycle>(), fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, null!, fixture.SourceProvider, fixture.Store, fixture.Paths, fixture.Installer,
                Substitute.For<IBasicTemplateLifecycle>(), fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, new(), null!, fixture.Store, fixture.Paths, fixture.Installer,
                Substitute.For<IBasicTemplateLifecycle>(), fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, new(), fixture.SourceProvider, null!, fixture.Paths, fixture.Installer,
                Substitute.For<IBasicTemplateLifecycle>(), fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, new(), fixture.SourceProvider, fixture.Store, null!, fixture.Installer,
                Substitute.For<IBasicTemplateLifecycle>(), fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, new(), fixture.SourceProvider, fixture.Store, fixture.Paths, null!,
                Substitute.For<IBasicTemplateLifecycle>(), fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, new(), fixture.SourceProvider, fixture.Store, fixture.Paths, fixture.Installer,
                null!, fileSystem),
            () => new BasicAcquisitionRunner(fixture.Catalog, new(), fixture.SourceProvider, fixture.Store, fixture.Paths, fixture.Installer,
                Substitute.For<IBasicTemplateLifecycle>(), null!),
        ];

        foreach (var constructor in constructors)
        {
            constructor.Should().Throw<ArgumentNullException>();
        }
    }

    [Fact]
    public async Task EntryPoints_NullArguments_RejectBeforeIO()
    {
        using var fixture = new BasicAcquisitionFixture();
        var inspector = new BasicArtifactInspector(new PhysicalFileSystem());
        var lifecycle = new BasicTemplateLifecycle(fixture.Templates, fixture.Payload, inspector, fixture.Ownership, new PhysicalFileSystem());
        Func<Task>[] calls =
        [
            () => fixture.Runner().RunAsync(null!, CancellationToken.None),
            () => inspector.ValidateAsync(null!, fixture.Artifact(), CancellationToken.None),
            () => inspector.ValidateAsync("test.nupkg", null!, CancellationToken.None),
            () => lifecycle.SelectAsync(null!, new(fixture.Request().Directory), CancellationToken.None),
            () => lifecycle.SelectAsync(new("Fixture.Basic", NuGet.Versioning.VersionRange.All, [fixture.Artifact()]), null!, CancellationToken.None),
            () => lifecycle.EnsureAsync(null!, new(fixture.Request().Directory), false, CancellationToken.None),
            () => lifecycle.EnsureAsync(fixture.Artifact(), null!, false, CancellationToken.None),
        ];

        foreach (var call in calls)
        {
            await call.Should().ThrowAsync<ArgumentNullException>();
        }
    }

    [Fact]
    public async Task DefaultPayloadSource_OfflineDoesNotCallFeedClient()
    {
        using var fixture = new BasicAcquisitionFixture();
        var opened = false;
        var source = new BasicPackagePayloadSource(_ =>
        {
            opened = true;
            throw new InvalidOperationException("Offline must not construct a feed client.");
        });

        Func<Task> acquire = () => source.OpenAsync(fixture.Artifact(), true, CancellationToken.None);

        await acquire.Should().ThrowAsync<BasicAcquisitionException>();
        opened.Should().BeFalse();
    }
}
