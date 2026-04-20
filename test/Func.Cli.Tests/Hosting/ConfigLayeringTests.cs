// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class ConfigLayeringTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigLayeringTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void SmartDefaults_DetectsWorkerRuntime_FromCsproj()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        Assert.Equal("dotnet-isolated", env["FUNCTIONS_WORKER_RUNTIME"]);
    }

    [Fact]
    public void SmartDefaults_DefaultsStorage_ToUseDevelopmentStorage()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        Assert.Equal("UseDevelopmentStorage=true", env["AzureWebJobsStorage"]);
    }

    [Fact]
    public void DotEnv_OverridesSmartDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, ".env"),
            "AzureWebJobsStorage=DefaultEndpointsProtocol=https;AccountName=real");

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        Assert.Equal("DefaultEndpointsProtocol=https;AccountName=real", env["AzureWebJobsStorage"]);
    }

    [Fact]
    public void LocalSettingsJson_OverridesDotEnv()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, ".env"),
            "MY_SETTING=from_env\nSHARED_KEY=env_value");
        File.WriteAllText(Path.Combine(_tempDir, "local.settings.json"), """
        {
            "Values": {
                "SHARED_KEY": "local_settings_value"
            }
        }
        """);

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        Assert.Equal("from_env", env["MY_SETTING"]);
        Assert.Equal("local_settings_value", env["SHARED_KEY"]);
    }

    [Fact]
    public void AppSettingsDevelopment_OverridesSmartDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.Development.json"), """
        {
            "Values": {
                "FUNCTIONS_WORKER_RUNTIME": "custom-override"
            }
        }
        """);

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        Assert.Equal("custom-override", env["FUNCTIONS_WORKER_RUNTIME"]);
    }

    [Fact]
    public void NoConfigFiles_StillGetsSmartDefaults()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        // Should at least have storage default
        Assert.Equal("UseDevelopmentStorage=true", env["AzureWebJobsStorage"]);
        // Standard env vars should be set
        Assert.True(env.ContainsKey("AZURE_FUNCTIONS_ENVIRONMENT"));
    }

    [Fact]
    public void FullLayering_HighestPriorityWins()
    {
        // Set up all layers
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project/>"); // smart default: dotnet-isolated

        File.WriteAllText(Path.Combine(_tempDir, ".env"),
            "LAYER_TEST=from_env\nFUNCTIONS_WORKER_RUNTIME=from_env");

        File.WriteAllText(Path.Combine(_tempDir, "appsettings.Development.json"), """
        {
            "Values": {
                "LAYER_TEST": "from_appsettings",
                "FUNCTIONS_WORKER_RUNTIME": "from_appsettings"
            }
        }
        """);

        File.WriteAllText(Path.Combine(_tempDir, "local.settings.json"), """
        {
            "Values": {
                "LAYER_TEST": "from_local_settings"
            }
        }
        """);

        var config = new HostConfiguration(_tempDir);

        var env = config.BuildEnvironment();

        // local.settings.json wins for LAYER_TEST (highest priority user config)
        Assert.Equal("from_local_settings", env["LAYER_TEST"]);
        // appsettings.Development.json wins for FUNCTIONS_WORKER_RUNTIME
        // (it overrides .env which overrides smart defaults)
        // But local.settings.json doesn't set it, so appsettings value persists
        Assert.Equal("from_appsettings", env["FUNCTIONS_WORKER_RUNTIME"]);
    }

    [Fact]
    public void AppSettingsDevelopment_ConnectionStrings_SetAsEnvVars()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.Development.json"), """
        {
            "ConnectionStrings": {
                "MyDb": "Server=localhost;Database=test"
            }
        }
        """);

        var config = new HostConfiguration(_tempDir);
        var env = config.BuildEnvironment();

        Assert.Equal("Server=localhost;Database=test", env["ConnectionStrings:MyDb"]);
    }

    [Fact]
    public void AppSettingsDevelopment_TopLevelKeys_SetAsEnvVars()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.Development.json"), """
        {
            "MY_CUSTOM_KEY": "top_level_value",
            "Values": {
                "INSIDE_VALUES": "nested_value"
            }
        }
        """);

        var config = new HostConfiguration(_tempDir);
        var env = config.BuildEnvironment();

        Assert.Equal("top_level_value", env["MY_CUSTOM_KEY"]);
        Assert.Equal("nested_value", env["INSIDE_VALUES"]);
    }

    [Fact]
    public void AppSettingsDevelopment_MalformedJson_Skipped()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "appsettings.Development.json"), "{ not valid json !!!");

        var config = new HostConfiguration(_tempDir);

        // Should not throw — malformed file is silently skipped
        var env = config.BuildEnvironment();
        Assert.NotNull(env);
    }

    [Fact]
    public void SmartDefaults_ProjectRoot_UsedForDetection_NotScriptRoot()
    {
        // Simulate the case where ScriptRoot is updated to build output
        // but ProjectRoot still points to the original project directory
        var projectDir = Path.Combine(_tempDir, "project");
        var buildDir = Path.Combine(_tempDir, "out", "bin");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(buildDir);

        File.WriteAllText(Path.Combine(projectDir, "MyApp.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(projectDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(buildDir, "host.json"), "{}");

        var config = new HostConfiguration(projectDir);
        config.UpdateScriptRoot(buildDir);

        var env = config.BuildEnvironment();

        // Should still detect dotnet-isolated from ProjectRoot, not the build output dir
        Assert.Equal("dotnet-isolated", env["FUNCTIONS_WORKER_RUNTIME"]);
    }

    [Fact]
    public void DotEnv_HandlesConnectionStringWithEquals()
    {
        File.WriteAllText(Path.Combine(_tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, ".env"),
            "AzureWebJobsStorage=DefaultEndpointsProtocol=https;AccountName=myacct;AccountKey=abc123==");

        var config = new HostConfiguration(_tempDir);
        var env = config.BuildEnvironment();

        Assert.Equal("DefaultEndpointsProtocol=https;AccountName=myacct;AccountKey=abc123==",
            env["AzureWebJobsStorage"]);
    }
}
