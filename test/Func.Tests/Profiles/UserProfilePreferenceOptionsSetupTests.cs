// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Profiles;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Azure.Functions.Cli.Tests.Profiles;

public sealed class UserProfilePreferenceOptionsSetupTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("user-profile-preferences-");
    private readonly DirectoryInfo _projectDir = Directory.CreateTempSubdirectory("project-profile-preferences-");

    public void Dispose()
    {
        _dir.Delete(recursive: true);
        _projectDir.Delete(recursive: true);
    }

    [Fact]
    public void Configure_BindsDefaultProfileFromUserConfiguration()
    {
        WriteUserConfig("""{"defaultProfile":" flex "}""");
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _dir);
        var setup = new UserProfilePreferenceOptionsSetup(defaultConfiguration, configurationProvider);
        UserProfilePreferenceOptions options = new();

        setup.Configure(options);

        Assert.Equal("flex", options.DefaultProfile);
    }

    [Fact]
    public void Configure_IgnoresProjectConfiguration()
    {
        WriteUserConfig("""{"defaultProfile":""}""");
        WriteProjectConfig("""{"defaultProfile":"project-profile"}""");
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _dir);
        var setup = new UserProfilePreferenceOptionsSetup(defaultConfiguration, configurationProvider);
        UserProfilePreferenceOptions options = new();

        setup.Configure(options);

        Assert.Null(options.DefaultProfile);
    }

    private void WriteUserConfig(string contents)
        => File.WriteAllText(Path.Combine(_dir.FullName, CliConfigurationNames.ConfigFileName), contents);

    private void WriteProjectConfig(string contents)
    {
        string folder = Path.Combine(_projectDir.FullName, CliConfigurationNames.ProjectConfigFolderName);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, CliConfigurationNames.ConfigFileName);
        File.WriteAllText(path, contents);
    }
}
