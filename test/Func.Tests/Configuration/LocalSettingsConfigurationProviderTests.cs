// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Configuration;

namespace Azure.Functions.Cli.Tests.Configuration;

public sealed class LocalSettingsConfigurationProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("local-settings-configuration-");
    private readonly DirectoryInfo _userDir = Directory.CreateTempSubdirectory("user-settings-configuration-");

    public void Dispose()
    {
        _dir.Delete(recursive: true);
        _userDir.Delete(recursive: true);
    }

    [Fact]
    public void Build_ProjectsOnlyCliRelevantLocalSettings()
    {
        WriteSettings(
            """
            {
              "Values": {
                "FUNCTIONS_WORKER_RUNTIME": "node",
                "AzureWebJobsStorage": "UseDevelopmentStorage=true"
              },
              "Host": {
                "LocalHttpPort": "7073",
                "CORS": "*",
                "CORSCredentials": "true"
              }
            }
            """);

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .Build();

        configuration["stack:runtime"].Should().Be("node");
        configuration["HostStartup:Port"].Should().Be("7073");
        configuration["HostStartup:Cors"].Should().Be("*");
        configuration["HostStartup:CorsCredentials"].Should().Be("True");
        configuration["AzureWebJobsStorage"].Should().BeNull();
    }

    [Fact]
    public void Build_ProjectConfigCanOverrideLocalSettingsProjection()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["stack:runtime"] = "python",
            })
            .Build();

        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);

        options.Runtime.Should().Be("python");
    }

    [Fact]
    public void Build_SourceOrderAppliesProjectLocalGlobalEnvironmentPrecedence()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"local"}}""");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["stack:runtime"] = "environment",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["stack:runtime"] = "global",
            })
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["stack:runtime"] = "project",
            })
            .Build();

        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);

        options.Runtime.Should().Be("project");
    }

    [Fact]
    public void Build_LocalSettingsProjectionOverridesGlobalAndEnvironmentSources()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"local"}}""");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["stack:runtime"] = "environment",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["stack:runtime"] = "global",
            })
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .Build();

        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);

        options.Runtime.Should().Be("local");
    }

    [Fact]
    public void StackOptionsSetup_NamedOptionsUseRequestedProjectDirectory()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"custom-runtime"}}""");
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);
        var setup = new StackOptionsSetup(defaultConfiguration, configurationProvider);
        StackOptions options = new();

        setup.Configure(_dir.FullName, options);

        options.Runtime.Should().Be("custom-runtime");
    }

    [Fact]
    public void HostStartupOptionsSetup_NamedOptionsUseRequestedProjectDirectory()
    {
        WriteSettings("""{"Host":{"LocalHttpPort":7074}}""");
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);
        var setup = new HostStartupOptionsSetup(defaultConfiguration, configurationProvider);
        HostStartupOptions options = new();

        setup.Configure(_dir.FullName, options);

        options.Port.Should().Be(7074);
    }

    [Fact]
    public void CliConfigurationProvider_ProjectConfigurationIncludesProjectSourcesOnly()
    {
        WriteUserConfig("""{"defaultProfile":"user-default","profiles":["user-profile"]}""");
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");
        WriteProjectConfig("""{"profiles":["project-profile"]}""");
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);

        IConfiguration configuration = configurationProvider.GetProjectConfiguration(_dir);

        configuration["profiles:0"].Should().Be("project-profile");
        configuration["defaultProfile"].Should().BeNull();
        configuration["stack:runtime"].Should().Be("node");
    }

    [Fact]
    public void CliConfigurationProvider_EffectiveConfigurationPreservesSourcePrecedence()
    {
        WriteUserConfig("""{"stack":{"runtime":"user"}}""");
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"local"}}""");
        WriteProjectConfig("""{"stack":{"runtime":"project"}}""");
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);

        IConfiguration configuration = configurationProvider.GetEffectiveConfiguration(_dir);

        configuration["stack:runtime"].Should().Be("project");
    }

    [Fact]
    public void CliConfigurationProvider_ReusesCachedProjectConfiguration()
    {
        WriteProjectConfig("""{"defaultProfile":"flex"}""");
        var configurationProvider = new CliConfigurationProvider(new LocalSettingsProvider(), _userDir);
        IConfiguration firstConfiguration = configurationProvider.GetProjectConfiguration(_dir);

        WriteProjectConfig("""{"defaultProfile":"linux-premium"}""");
        IConfiguration secondConfiguration = configurationProvider.GetProjectConfiguration(_dir);

        secondConfiguration.Should().BeSameAs(firstConfiguration);
        secondConfiguration["defaultProfile"].Should().Be("flex");
    }

    private void WriteSettings(string contents)
        => File.WriteAllText(Path.Combine(_dir.FullName, CliConfigurationNames.LocalSettingsFileName), contents);

    private void WriteUserConfig(string contents)
        => File.WriteAllText(Path.Combine(_userDir.FullName, CliConfigurationNames.ConfigFileName), contents);

    private void WriteProjectConfig(string contents)
    {
        string folder = Path.Combine(_dir.FullName, CliConfigurationNames.ProjectConfigFolderName);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, CliConfigurationNames.ConfigFileName);
        File.WriteAllText(path, contents);
    }
}
