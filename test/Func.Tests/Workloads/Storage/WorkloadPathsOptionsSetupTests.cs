// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadPathsOptionsSetupTests
{
    [Fact]
    public void Configure_UsesEnvironmentVariable_WhenExplicitlySet()
    {
        var overridden = Path.Combine(Path.GetTempPath(), "func-cli-home-override", Guid.NewGuid().ToString("N"));
        IEnvironmentVariables env = Substitute.For<IEnvironmentVariables>();
        env.Get(Constants.WorkloadsHomeEnvironmentVariable).Returns(overridden);

        var setup = new WorkloadPathsOptionsSetup(env);
        var options = new WorkloadPathsOptions();

        setup.Configure(options);

        Assert.Equal(Path.GetFullPath(overridden), options.Home);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configure_FallsBackToUserProfile_WhenEnvVarMissingOrWhitespace(string? value)
    {
        // Null/empty/whitespace all mean "not explicitly set" so the default
        // user-profile path wins; mirrors how Environment exposes an unset var.
        IEnvironmentVariables env = Substitute.For<IEnvironmentVariables>();
        env.Get(Constants.WorkloadsHomeEnvironmentVariable).Returns(value);

        var setup = new WorkloadPathsOptionsSetup(env);
        var options = new WorkloadPathsOptions();

        setup.Configure(options);

        var expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName));
        Assert.Equal(expected, options.Home);
    }

    [Fact]
    public void Constructor_ThrowsWhenEnvironmentIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadPathsOptionsSetup(null!));
    }

    [Fact]
    public void Configure_ThrowsWhenOptionsIsNull()
    {
        var setup = new WorkloadPathsOptionsSetup(Substitute.For<IEnvironmentVariables>());

        Assert.Throws<ArgumentNullException>(() => setup.Configure(null!));
    }
}
