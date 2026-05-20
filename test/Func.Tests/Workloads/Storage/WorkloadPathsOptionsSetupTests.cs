// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadPathsOptionsSetupTests
{
    [Fact]
    public void Configure_UsesHostConfiguration_WhenExplicitlySet()
    {
        var overridden = Path.Combine(Path.GetTempPath(), "func-cli-home-override", Guid.NewGuid().ToString("N"));
        IHostConfiguration host = BuildHostConfiguration(
            (Constants.WorkloadsHomeEnvironmentVariable, overridden));

        var setup = new WorkloadPathsOptionsSetup(host);
        var options = new WorkloadPathsOptions();

        setup.Configure(options);

        Assert.Equal(Path.GetFullPath(overridden), options.Home);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Configure_FallsBackToUserProfile_WhenHostValueMissingOrWhitespace(string? value)
    {
        // Null/empty/whitespace all mean "not explicitly set" so the default
        // user-profile path wins; mirrors how IConfiguration exposes an
        // unset key (null) and how an env-var-fed key surfaces empty values.
        IHostConfiguration host = BuildHostConfiguration(
            (Constants.WorkloadsHomeEnvironmentVariable, value));

        var setup = new WorkloadPathsOptionsSetup(host);
        var options = new WorkloadPathsOptions();

        setup.Configure(options);

        var expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName));
        Assert.Equal(expected, options.Home);
    }

    [Fact]
    public void Constructor_ThrowsWhenHostConfigurationIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkloadPathsOptionsSetup(null!));
    }

    [Fact]
    public void Configure_ThrowsWhenOptionsIsNull()
    {
        var setup = new WorkloadPathsOptionsSetup(BuildHostConfiguration());

        Assert.Throws<ArgumentNullException>(() => setup.Configure(null!));
    }

    private static IHostConfiguration BuildHostConfiguration(params (string Key, string? Value)[] entries)
    {
        var data = entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        IConfiguration inner = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
        return new HostConfiguration(inner);
    }
}
