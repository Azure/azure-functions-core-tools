// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Azure.Functions.Cli.Tests.Configuration;

public sealed class LocalSettingsConfigurationProviderTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("local-settings-configuration-");

    public void Dispose() => _dir.Delete(recursive: true);

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

        Assert.Equal("node", configuration["Stack:Runtime"]);
        Assert.Equal("7073", configuration["HostStartup:Port"]);
        Assert.Equal("*", configuration["HostStartup:Cors"]);
        Assert.Equal("True", configuration["HostStartup:CorsCredentials"]);
        Assert.Null(configuration["AzureWebJobsStorage"]);
    }

    [Fact]
    public void Build_ProjectConfigCanOverrideLocalSettingsProjection()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"node"}}""");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stack:Runtime"] = "python",
            })
            .Build();

        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);

        Assert.Equal("python", options.Runtime);
    }

    [Fact]
    public void Build_SourceOrderAppliesProjectLocalGlobalEnvironmentPrecedence()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"local"}}""");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stack:Runtime"] = "environment",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stack:Runtime"] = "global",
            })
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stack:Runtime"] = "project",
            })
            .Build();

        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);

        Assert.Equal("project", options.Runtime);
    }

    [Fact]
    public void Build_LocalSettingsProjectionOverridesGlobalAndEnvironmentSources()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"local"}}""");

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stack:Runtime"] = "environment",
            })
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Stack:Runtime"] = "global",
            })
            .Add(new LocalSettingsConfigurationSource(_dir, new LocalSettingsProvider()))
            .Build();

        StackOptions options = new();
        new StackOptionsSetup(configuration).Configure(options);

        Assert.Equal("local", options.Runtime);
    }

    [Fact]
    public void StackOptionsSetup_NamedOptionsUseRequestedProjectDirectory()
    {
        WriteSettings("""{"Values":{"FUNCTIONS_WORKER_RUNTIME":"custom-runtime"}}""");
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var sourceBuilder = new CliConfigurationSourceBuilder(new LocalSettingsProvider());
        var setup = new StackOptionsSetup(defaultConfiguration, sourceBuilder);
        StackOptions options = new();

        setup.Configure(_dir.FullName, options);

        Assert.Equal("custom-runtime", options.Runtime);
    }

    [Fact]
    public void HostStartupOptionsSetup_NamedOptionsUseRequestedProjectDirectory()
    {
        WriteSettings("""{"Host":{"LocalHttpPort":7074}}""");
        IConfigurationRoot defaultConfiguration = new ConfigurationBuilder().Build();
        var sourceBuilder = new CliConfigurationSourceBuilder(new LocalSettingsProvider());
        var setup = new HostStartupOptionsSetup(defaultConfiguration, sourceBuilder);
        HostStartupOptions options = new();

        setup.Configure(_dir.FullName, options);

        Assert.Equal(7074, options.Port);
    }

    private void WriteSettings(string contents)
        => File.WriteAllText(Path.Combine(_dir.FullName, CliConfigurationNames.LocalSettingsFileName), contents);
}
