// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class HostConfigurationTests
{
    [Fact]
    public void BuildEnvironment_SetsRequiredCoreToolsVariables()
    {
        var config = new HostConfiguration("/app/myfunction") { Port = 7071 };

        var env = config.BuildEnvironment();

        Assert.Equal("true", env["FUNCTIONS_CORETOOLS_ENVIRONMENT"]);
        Assert.Equal("Development", env["AZURE_FUNCTIONS_ENVIRONMENT"]);
        Assert.Equal("/app/myfunction", env["AzureWebJobsScriptRoot"]);
        Assert.Equal("http://localhost:7071", env["ASPNETCORE_URLS"]);
        Assert.Equal("localhost:7071", env["WEBSITE_HOSTNAME"]);
        Assert.Equal("true", env["AzureFunctionsJobHost__SequentialRestart"]);
    }

    [Fact]
    public void BuildEnvironment_UsesCustomPort()
    {
        var config = new HostConfiguration("/app") { Port = 9090 };

        var env = config.BuildEnvironment();

        Assert.Equal("http://localhost:9090", env["ASPNETCORE_URLS"]);
        Assert.Equal("localhost:9090", env["WEBSITE_HOSTNAME"]);
    }

    [Fact]
    public void BuildEnvironment_SetsFunctionsFilter()
    {
        var config = new HostConfiguration("/app")
        {
            FunctionsFilter = ["HttpTrigger1", "QueueTrigger1"]
        };

        var env = config.BuildEnvironment();

        Assert.Equal("HttpTrigger1", env["AzureFunctionsJobHost__functions__0"]);
        Assert.Equal("QueueTrigger1", env["AzureFunctionsJobHost__functions__1"]);
    }

    [Fact]
    public void BuildEnvironment_NoFunctionsFilter_DoesNotSetFilterVars()
    {
        var config = new HostConfiguration("/app");

        var env = config.BuildEnvironment();

        Assert.DoesNotContain(env, kv => kv.Key.StartsWith("AzureFunctionsJobHost__functions__"));
    }

    [Fact]
    public void BuildEnvironment_SetsCorsVariables()
    {
        var config = new HostConfiguration("/app")
        {
            CorsOrigins = "http://localhost:3000,http://localhost:4200",
            CorsCredentials = true
        };

        var env = config.BuildEnvironment();

        Assert.Equal("http://localhost:3000,http://localhost:4200", env["Host__Cors__AllowedOrigins"]);
        Assert.Equal("true", env["Host__Cors__SupportCredentials"]);
    }

    [Fact]
    public void BuildEnvironment_NoCors_DoesNotSetCorsVars()
    {
        var config = new HostConfiguration("/app");

        var env = config.BuildEnvironment();

        Assert.DoesNotContain("Host__Cors__AllowedOrigins", env.Keys);
    }

    [Fact]
    public void BuildEnvironment_LoadsLocalSettingsJson()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "local.settings.json"), """
            {
              "IsEncrypted": false,
              "Values": {
                "AzureWebJobsStorage": "UseDevelopmentStorage=true",
                "FUNCTIONS_WORKER_RUNTIME": "node",
                "MY_CUSTOM_SETTING": "hello"
              },
              "ConnectionStrings": {
                "SqlConn": "Server=localhost"
              }
            }
            """);

            var config = new HostConfiguration(tempDir);
            var env = config.BuildEnvironment();

            Assert.Equal("node", env["FUNCTIONS_WORKER_RUNTIME"]);
            Assert.Equal("hello", env["MY_CUSTOM_SETTING"]);
            Assert.Equal("Server=localhost", env["ConnectionStrings__SqlConn"]);

            // Core vars override local.settings if they overlap
            Assert.Equal("Development", env["AZURE_FUNCTIONS_ENVIRONMENT"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void BuildEnvironment_EnableAuth_OmitsCoreToolsEnv()
    {
        var config = new HostConfiguration("/app") { EnableAuth = true };

        var env = config.BuildEnvironment();

        Assert.DoesNotContain("FUNCTIONS_CORETOOLS_ENVIRONMENT", env.Keys);
    }

    [Fact]
    public void ValidateScriptRoot_NoHostJson_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var interaction = new TestInteractionService();

            var result = HostConfiguration.ValidateScriptRoot(tempDir, interaction);

            Assert.False(result);
            Assert.Contains(interaction.Lines, l => l.Contains("host.json"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ValidateScriptRoot_WithHostJson_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "host.json"), "{}");
            var interaction = new TestInteractionService();

            var result = HostConfiguration.ValidateScriptRoot(tempDir, interaction);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
