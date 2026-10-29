// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.PackageIntake;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static Azure.Functions.Cli.Tests.Templates.PackageIntake.TemplatePackageTestData;

namespace Azure.Functions.Cli.Tests.Templates.PackageIntake;

public sealed class TemplatePackageContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative.nupkg")]
    public void Request_InvalidPath_Throws(string? path)
    {
        Action action = () => new LocalTemplatePackageRequest(path!, PackageId, PackageVersion, "Template");

        action.Should().Throw<ArgumentException>().WithParameterName("archivePath");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad/package")]
    public void Request_InvalidPackageId_Throws(string? id)
    {
        Action action = () => Request(id: id!);

        action.Should().Throw<ArgumentException>().WithParameterName("expectedPackageId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.*")]
    [InlineData("[1.0,2.0)")]
    [InlineData("1.2")]
    [InlineData("1.2.3+metadata")]
    [InlineData("01.2.3")]
    public void Request_InvalidOrNoncanonicalVersion_Throws(string? version)
    {
        Action action = () => Request(version: version!);

        action.Should().Throw<ArgumentException>().WithParameterName("expectedVersion");
    }

    [Fact]
    public void Request_NormalizedPrereleaseVersion_IsAccepted()
    {
        var request = Request(version: "1.2.3-preview.4");

        request.ExpectedVersion.Should().Be("1.2.3-preview.4");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Type With Spaces")]
    [InlineData("type/escape")]
    public void Request_InvalidPackageType_Throws(string? type)
    {
        Action action = () => Request(type: type!);

        action.Should().Throw<ArgumentException>().WithParameterName("expectedPackageType");
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Request_InvalidDigest_Throws(string digest)
    {
        Action action = () => Request(digest: digest);

        action.Should().Throw<ArgumentException>().WithParameterName("expectedSha256");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limits_EachNonpositiveDomain_Throws(int value)
    {
        Action[] actions =
        [
            () => Limits(archiveBytes: value),
            () => Limits(entries: value),
            () => Limits(expandedBytes: value),
            () => Limits(entryBytes: value),
            () => Limits(manifestBytes: value),
            () => Limits(paths: value),
            () => Limits(pathLength: value),
            () => Limits(bufferBytes: value),
            () => Limits(activeLeases: value),
        ];

        foreach (Action action in actions)
        {
            action.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Limits_ArchiveBeyondManagedArrayCapacity_Throws()
    {
        Action action = () => Limits(archiveBytes: int.MaxValue);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxArchiveBytes");
    }

    [Fact]
    public void Limits_ManifestBeyondManagedArrayCapacity_Throws()
    {
        Action action = () => Limits(manifestBytes: int.MaxValue);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("maxManifestBytes");
    }

    [Fact]
    public void Constructor_NullDependencies_Throw()
    {
        Action missingFileSystem = () => new LocalTemplatePackageIntake(null!, Limits());
        Action missingLimits = () => new LocalTemplatePackageIntake(Substitute.For<IFileSystem>(), null!);

        missingFileSystem.Should().Throw<ArgumentNullException>().WithParameterName("fileSystem");
        missingLimits.Should().Throw<ArgumentNullException>().WithParameterName("limits");
    }

    [Fact]
    public void AddTemplatePackageIntake_ExplicitLimits_RegistersSingletonAndPreservesExistingFileSystem()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        ServiceCollection services = new();
        services.AddSingleton(fileSystem);
        var limits = Limits();

        services.AddTemplatePackageIntake(limits);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        provider.GetRequiredService<IFileSystem>().Should().BeSameAs(fileSystem);
        provider.GetRequiredService<TemplatePackageIntakeLimits>().Should().BeSameAs(limits);
        provider.GetRequiredService<LocalTemplatePackageIntake>().Should().BeSameAs(provider.GetRequiredService<LocalTemplatePackageIntake>());
    }

    [Fact]
    public void AddTemplatePackageIntake_NullArguments_Throw()
    {
        Action missingServices = () => TemplatePackageServiceCollectionExtensions.AddTemplatePackageIntake(null!, Limits());
        Action missingLimits = () => new ServiceCollection().AddTemplatePackageIntake(null!);

        missingServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        missingLimits.Should().Throw<ArgumentNullException>().WithParameterName("limits");
    }
}
