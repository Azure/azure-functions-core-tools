// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Tests.Profiles;

public sealed class ProjectProfileOptionsSetupTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("project-profile-options-");
    private readonly DirectoryInfo _userDir = Directory.CreateTempSubdirectory("user-profile-options-");

    public void Dispose()
    {
        _dir.Delete(recursive: true);
        _userDir.Delete(recursive: true);
    }

    [Fact]
    public void Configure_ReturnsEmpty_WhenConfigurationMissing()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder().Build();
        var setup = new ProjectProfileOptionsSetup(configuration);
        ProjectProfileOptions options = new();

        setup.Configure(options);

        options.Profiles.Should().BeEmpty();
        options.DefaultProfile.Should().BeNull();
    }

    [Fact]
    public void Configure_NamedOptionsUseRequestedProjectDirectory()
    {
        WriteProjectConfig(
            """
            {
              "profiles": [ "flex", "linux-premium" ],
              "defaultProfile": "linux-premium"
            }
            """);
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);
        var setup = new ProjectProfileOptionsSetup(defaultConfiguration, configurationProvider);
        ProjectProfileOptions options = new();

        setup.Configure(_dir.FullName, options);

        options.Profiles.Should().Equal(["flex", "linux-premium"]);
        options.DefaultProfile.Should().Be("linux-premium");
    }

    [Fact]
    public void Configure_NamedOptionsIgnoreUserConfiguration()
    {
        WriteUserConfig(
            """
            {
              "profiles": [ "user-profile" ],
              "defaultProfile": "user-profile"
            }
            """);
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);
        var setup = new ProjectProfileOptionsSetup(defaultConfiguration, configurationProvider);
        ProjectProfileOptions options = new();

        setup.Configure(_dir.FullName, options);

        options.Profiles.Should().BeEmpty();
        options.DefaultProfile.Should().BeNull();
    }

    [Fact]
    public void Configure_RejectsDuplicateProfiles()
    {
        IConfigurationRoot configuration = BuildJsonConfiguration(
            """
            {
              "profiles": [ "flex", "FLEX" ]
            }
            """);
        var setup = new ProjectProfileOptionsSetup(configuration);
        ProjectProfileOptions options = new();

        ProfileConfigurationException ex = FluentActions.Invoking(() => setup.Configure(options)).Should().ThrowExactly<ProfileConfigurationException>().Which;

        ex.Message.Should().ContainEquivalentOf("more than once");
    }

    [Fact]
    public void Configure_RejectsUnsupportedSchema()
    {
        IConfigurationRoot configuration = BuildJsonConfiguration(
            """
            {
              "$schema": "https://aka.ms/func-config/v999/schema.json"
            }
            """);
        var setup = new ProjectProfileOptionsSetup(configuration);
        ProjectProfileOptions options = new();

        ProfileConfigurationException ex = FluentActions.Invoking(() => setup.Configure(options)).Should().ThrowExactly<ProfileConfigurationException>().Which;

        ex.Message.Should().ContainEquivalentOf("unsupported schema");
        ex.Message.Should().Contain(ProfileSchemas.ProjectConfigV1);
    }

    private void WriteProjectConfig(string contents)
    {
        string folder = Path.Combine(_dir.FullName, CliConfigurationNames.ProjectConfigFolderName);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, CliConfigurationNames.ConfigFileName);
        File.WriteAllText(path, contents);
    }

    private void WriteUserConfig(string contents)
        => File.WriteAllText(Path.Combine(_userDir.FullName, CliConfigurationNames.ConfigFileName), contents);

    private static IConfigurationRoot BuildJsonConfiguration(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream(bytes);
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }
}
