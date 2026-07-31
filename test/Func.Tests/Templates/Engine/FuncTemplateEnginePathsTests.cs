// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Tests.Templates.Engine;

public class FuncTemplateEnginePathsTests
{
    [Fact]
    public void Constructor_RelocatesHiveUnderFuncHomeScopedByVersion()
    {
        var funcHome = Path.Combine(Path.GetTempPath(), "func-home");

        var paths = new FuncTemplateEnginePaths(funcHome, Path.GetTempPath(), "5.0.0-preview.2");

        var expectedGlobal = Path.Combine(Path.GetFullPath(funcHome), "template-engine");
        paths.GlobalSettingsDir.Should().Be(expectedGlobal);
        paths.HostSettingsDir.Should().Be(Path.Combine(expectedGlobal, "func"));
        paths.HostVersionSettingsDir.Should().Be(Path.Combine(expectedGlobal, "func", "5.0.0-preview.2"));
    }

    [Fact]
    public void Constructor_NormalisesFuncHomeAndUserProfileWithGetFullPath()
    {
        var paths = new FuncTemplateEnginePaths("relative/home", "relative/profile", "1.0.0");

        paths.GlobalSettingsDir.Should().Be(Path.Combine(Path.GetFullPath("relative/home"), "template-engine"));
        paths.UserProfileDir.Should().Be(Path.GetFullPath("relative/profile"));
    }

    [Fact]
    public void GlobalSettingsDir_IsAboveHostDirs_SoTheGlobalPackageStoreIsRelocatedToo()
    {
        // The isolation finding: overriding only the host dirs leaves the
        // engine's global package store under ~/.templateengine. All three
        // dirs must live under the func home.
        var paths = new FuncTemplateEnginePaths(Path.Combine(Path.GetTempPath(), "fh"), Path.GetTempPath(), "1.0.0");

        paths.HostSettingsDir.Should().StartWith(paths.GlobalSettingsDir);
        paths.HostVersionSettingsDir.Should().StartWith(paths.HostSettingsDir);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsForMissingFuncHome(string? funcHome)
    {
        FluentActions.Invoking(() => new FuncTemplateEnginePaths(funcHome!, Path.GetTempPath(), "1.0.0"))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsForMissingUserProfile(string? userProfile)
    {
        FluentActions.Invoking(() => new FuncTemplateEnginePaths(Path.GetTempPath(), userProfile!, "1.0.0"))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsForMissingCliVersion(string? cliVersion)
    {
        FluentActions.Invoking(() => new FuncTemplateEnginePaths(Path.GetTempPath(), Path.GetTempPath(), cliVersion!))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ThrowsForNullVersionProvider()
    {
        FluentActions.Invoking(() => new FuncTemplateEnginePaths(null!)).Should().Throw<ArgumentNullException>();
    }
}
