using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Tests.E2E.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using Azure.Functions.Cli.Common;
using Microsoft.Azure.WebJobs.Script.Description.DotNet.CSharp.Analyzers;

namespace Azure.Functions.Cli.Tests.E2E
{
    public class StartTests : BaseE2ETest
    {
        private const string _serverNotReady = "Host was not ready after 10 seconds";
        public StartTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task start_nodejs()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Functions:",
                    "HttpTrigger: [GET,POST] http://localhost:7071/api/HttpTrigger"
                },
                OutputDoesntContain = new string[]
                {
                        "Initializing function HTTP routes",
                        "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                },
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello, Test!", because: "response from default function should be 'Hello, {name}!'");
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(120),
            }, _output);
        }

        [Fact]
        public async Task start_nodejs_v3()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node -m v3",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Functions:",
                    "HttpTrigger: [GET,POST] http://localhost:7071/api/HttpTrigger"
                },
                OutputDoesntContain = new string[]
                {
                        "Initializing function HTTP routes",
                        "Content root path:" // ASPNETCORE_SUPPRESSSTATUSMESSAGES is set to true by default
                },
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task start_nodejs_with_inspect()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start --verbose --language-worker -- \"--inspect=5050\""
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Debugger listening on ws://127.0.0.1:5050"
                },
                Test = async (_, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    p.Kill();
                },
                CommandTimeout = TimeSpan.FromSeconds(120)
            }, _output);

        }

        [Fact]
        public async Task start_nodejs_loglevel_overrriden_in_settings()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime node",
                    "settings add AzureFunctionsJobHost__logging__logLevel__Default Debug",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                OutputContains = new[]
                {
                    "Workers Directory set to"
                },
                Test = async (_, p) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(15));
                    p.Kill();
                },
                CommandTimeout = TimeSpan.FromSeconds(120),
            }, _output);
        }

        [Fact]
        public async Task start_loglevel_overrriden_in_host_json()
        {
            var functionName = "HttpTriggerCSharp";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        $"new --template Httptrigger --name {functionName}",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "Host configuration applied."
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                },
            }, _output, startHost: true);
        }

        [Fact]
        public async Task start_loglevel_overrriden_in_host_json_category_filter()
        {
            var functionName = "HttpTriggerCSharp";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        $"new --template Httptrigger --name {functionName}",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "Found the following functions:"
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Reading host configuration file"
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                },
            }, _output, startHost: true);
        }

        [Fact]
        public async Task start_loglevel_None_overrriden_in_host_json()
        {
            var functionName = "HttpTrigger";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        $"new --template Httptrigger --name {functionName}",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\"}}}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "Worker process started and initialized"
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Initializing function HTTP routes"
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                },
            }, _output, startHost: true);
        }

        [Fact]
        public async Task start_dotnet_csharp()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime dotnet",
                    "new --template Httptrigger --name HttpTrigger",
                    "start --build --port 7073"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7073") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task start_displays_error_on_invalid_function_json()
        {
            var functionName = "HttpTriggerJS";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node -m v3",
                        $"new --template \"Http Trigger\" --name {functionName}",
                    },
                    Test = async (workingDir, _) =>
                    {
                        var filePath = Path.Combine(workingDir, functionName, "function.json");
                        var functionJson = await File.ReadAllTextAsync(filePath);
                        functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
                        await File.WriteAllTextAsync(filePath, functionJson);
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = false,
                    OutputContains = new []
                    {
                        "The binding type(s) 'http2' were not found in the configured extension bundle. Please ensure the type is correct and the correct version of extension bundle is configured."
                    },
                    Test = async (_, p) =>
                    {
                        // give the host time to load functions and print any errors
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        p.Kill();
                    }
                }
            }, _output);
        }

        [Fact]
        public async Task start_displays_error_on_invalid_host_json()
        {
            var functionName = "HttpTriggerCSharp";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        $"new --template Httptrigger --name {functionName}",

                    },
                    Test = async (workingDir, p) =>
                    {
                        var filePath = Path.Combine(workingDir, "host.json");
                        string hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
                        await File.WriteAllTextAsync(filePath, hostJsonContent);
                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Extension bundle configuration should not be present" },
                },
            }, _output, startHost: true);
        }


        [Fact]
        public async Task start_displays_error_on_missing_host_json()
        {
            var functionName = "HttpTriggerCSharp";

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        $"new --template Httptrigger --name {functionName}",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var hostJsonPath = Path.Combine(workingDir, "host.json");
                        File.Delete(hostJsonPath);

                    },
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start"
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Host.json file in missing" },
                },
            }, _output);
        }

        [Fact]
        public async Task start_host_port_in_use()
        {
            var tcpListner = new TcpListener(IPAddress.Any, 8081);
            try
            {
                tcpListner.Start();

                await CliTester.Run(new RunConfiguration
                {
                    Commands = new[]
                    {
                    "init . --worker-runtime node",
                    "new --template \"Http Trigger\" --name HttpTrigger",
                    "start --port 8081"
                },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Port 8081 is unavailable" },
                    CommandTimeout = TimeSpan.FromSeconds(120),
                }, _output);
            }
            finally
            {
                tcpListner.Stop();
            }
        }

        [Fact]
        public async Task start_handles_empty_envvars_correctly()
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime node",
                        "new --template \"Http trigger\" --name HttpTrigger",
                        "settings add emptySetting EMPTY_VALUE",
                    },
                    Test = async (workingDir, p) =>
                    {
                        var settingsFile = Path.Combine(workingDir, "local.settings.json");
                        var content = File.ReadAllText(settingsFile);
                        content = content.Replace("EMPTY_VALUE", "");
                        File.WriteAllText(settingsFile,content);
                    }
                },
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "start --port 6543"
                    },
                    ExpectExit = false,
                    Test = async (w, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:6543/") })
                        {
                            client.Timeout = TimeSpan.FromSeconds(2);
                            for (var i = 0; i < 10; i++)
                            {
                                try
                                {
                                    var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                                    response.EnsureSuccessStatusCode();
                                    break;
                                }
                                catch
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(2));
                                }
                            }
                        }
                        p.Kill();
                    },
                    OutputDoesntContain = new string[]
                    {
                        "Skipping 'emptySetting' from local settings as it's already defined in current environment variables."
                    }
                }
            }, _output);
        }

        [Fact(Skip = "Flaky test")]
        public async Task start_powershell()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime powershell --managed-dependencies false",
                    "new --template \"Http trigger\" --name HttpTrigger",
                    "start"
                },
                ExpectExit = false,
                CommandTimeout = TimeSpan.FromMinutes(1),
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/HttpTrigger?name=Test");
                        var result = await response.Content.ReadAsStringAsync();
                        p.Kill();
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        result.Should().Be("Hello, Test. This HTTP triggered function executed successfully.", because: "response from default function should be 'Hello, {name}. This HTTP triggered function executed successfully.'");
                    }
                },
            }, _output);
        }

        [Fact]
        public async Task only_run_some_functions()
        {
            await CliTester.Run(new RunConfiguration
            {
                Commands = new[]
                {
                    "init . --worker-runtime javascript",
                    "new --template \"Http trigger\" --name http1",
                    "new --template \"Http trigger\" --name http2",
                    "new --template \"Http trigger\" --name http3",
                    "start --functions http2 http1"
                },
                ExpectExit = false,
                Test = async (workingDir, p) =>
                {
                    using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                    {
                        (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                        var response = await client.GetAsync("/api/http1?name=Test");
                        response.StatusCode.Should().Be(HttpStatusCode.OK);

                        response = await client.GetAsync("/api/http2?name=Test");
                        response.StatusCode.Should().Be(HttpStatusCode.OK);

                        response = await client.GetAsync("/api/http3?name=Test");
                        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
                        p.Kill();
                    }
                },
                CommandTimeout = TimeSpan.FromSeconds(120)
            }, _output);
        }

        [Theory]
        [InlineData("dotnet")]
        [InlineData("dotnet-isolated")]
        public async Task start_with_user_secrets(string language)
        {
            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        $"init . --worker-runtime {language}",
                        "new --template \"Http trigger\" --name http1",
                        "new --template \"Queue trigger\" --name queue1"
                    },
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        // add connection string setting to queue code
                        var queueCodePath = Path.Combine(workingDir, "queue1.cs");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {queueCodePath}");
                        StringBuilder queueCodeStringBuilder = new StringBuilder();
                        using (StreamReader sr = File.OpenText(queueCodePath))
                        {
                            string s = "";
                            while ((s = sr.ReadLine()) != null)
                            {
                                queueCodeStringBuilder.Append(s);
                            }
                        }
                        var queueCodeString = queueCodeStringBuilder.ToString();
                        _output.WriteLine($"Old Queue File: {queueCodeString}");
                        var replacedText = queueCodeString.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
                        _output.WriteLine($"New Queue File: {replacedText}");
                        File.WriteAllText(queueCodePath, replacedText);

                        // clear local.settings.json
                        var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
                            { Constants.FunctionsWorkerRuntime, "dotnet" },
                            { "ConnectionStrings:MyQueueConn", "DefaultEndpointsProtocol=https;AccountName=storagesample;AccountKey=GMuzNHjlB3S9itqZJHHCnRkrokLkcSyW7yK9BRbGp0ENePunLPwBgpxV1Z/pVo9zpem/2xSHXkMqTHHLcx8XRA==EndpointSuffix=core.windows.net" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        "start --functions http1 --csharp",
                    },
                    ExpectExit = false,
                    OutputContains = new string[]
                    {
                        "Using for user secrets file configuration."
                    },
                    Test = async (workingDir, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/http1?name=Test");
                            response.StatusCode.Should().Be(HttpStatusCode.OK);
                            p.Kill();
                        }
                    }
                }
            }, _output);
        }

        [Fact(Skip = "Flaky test")]
        public async Task start_with_user_secrets_missing_storage()
        {
            string AzureWebJobsStorageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            Skip.If(!string.IsNullOrEmpty(AzureWebJobsStorageConnectionString),
                reason: "AzureWebJobsStorage should be not set to verify this test.");

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        "new --template \"Http trigger\" --name http1",
                        "new --template \"Queue trigger\" --name queue1"
                    },
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        // add connection string setting to queue code
                        var queueCodePath = Path.Combine(workingDir, "queue1.cs");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {queueCodePath}");
                        StringBuilder queueCodeStringBuilder = new StringBuilder();
                        using (StreamReader sr = File.OpenText(queueCodePath))
                        {
                            string s = "";
                            while ((s = sr.ReadLine()) != null)
                            {
                                queueCodeStringBuilder.Append(s);
                            }
                        }
                        var queueCodeString = queueCodeStringBuilder.ToString();
                        _output.WriteLine($"Old Queue File: {queueCodeString}");
                        var replacedText = queueCodeString.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
                        _output.WriteLine($"New Queue File: {replacedText}");
                        File.WriteAllText(queueCodePath, replacedText);

                        // clear local.settings.json
                        var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.FunctionsWorkerRuntime, "dotnet" },
                            { "ConnectionStrings:MyQueueConn", "DefaultEndpointsProtocol=https;AccountName=storagesample;AccountKey=GMuzNHjlB3S9itqZJHHCnRkrokLkcSyW7yK9BRbGp0ENePunLPwBgpxV1Z/pVo9zpem/2xSHXkMqTHHLcx8XRA==EndpointSuffix=core.windows.net" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        "start --functions http1 --csharp",
                    },
                    ExpectExit = true,
                    ExitInError = true,
                    ErrorContains = new[] { "Missing value for AzureWebJobsStorage in local.settings.json and User Secrets. This is required for all triggers other than httptrigger, kafkatrigger. You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in local.settings.json or User Secrets." },
                }
            }, _output);
        }

        [Fact(Skip = "Flaky test")]
        public async Task start_with_user_secrets_missing_binding_setting()
        {
            string AzureWebJobsStorageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            Skip.If(!string.IsNullOrEmpty(AzureWebJobsStorageConnectionString),
                reason: "AzureWebJobsStorage should be not set to verify this test.");

            await CliTester.Run(new RunConfiguration[]
            {
                new RunConfiguration
                {
                    Commands = new[]
                    {
                        "init . --worker-runtime dotnet",
                        "new --template \"Http trigger\" --name http1",
                        "new --template \"Queue trigger\" --name queue1"
                    },
                },
                new RunConfiguration
                {
                    PreTest = (workingDir) =>
                    {
                        // add connection string setting to queue code
                        var queueCodePath = Path.Combine(workingDir, "queue1.cs");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {queueCodePath}");
                        StringBuilder queueCodeStringBuilder = new StringBuilder();
                        using (StreamReader sr = File.OpenText(queueCodePath))
                        {
                            string s = "";
                            while ((s = sr.ReadLine()) != null)
                            {
                                queueCodeStringBuilder.Append(s);
                            }
                        }
                        var queueCodeString = queueCodeStringBuilder.ToString();
                        _output.WriteLine($"Old Queue File: {queueCodeString}");
                        var replacedText = queueCodeString.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
                        _output.WriteLine($"New Queue File: {replacedText}");
                        File.WriteAllText(queueCodePath, replacedText);

                        // clear local.settings.json
                        var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
                        Assert.True(File.Exists(queueCodePath));
                        _output.WriteLine($"Writing to file {localSettingsPath}");
                        File.WriteAllText(localSettingsPath, "{ \"IsEncrypted\": false, \"Values\": {} }");

                        // init and set user secrets
                        Dictionary<string, string> userSecrets = new Dictionary<string, string>()
                        {
                            { Constants.AzureWebJobsStorage, "UseDevelopmentStorage=true" },
                            { Constants.FunctionsWorkerRuntime, "dotnet" },
                        };
                        SetUserSecrets(workingDir, userSecrets);
                    },
                    Commands = new[]
                    {
                        "start --functions http1 --csharp",
                    },
                    ExpectExit = false,
                    OutputContains = new[]
                    {
                        "Warning: Cannot find value named 'ConnectionStrings:MyQueueConn' in local.settings.json or User Secrets that matches 'connection' property set on 'queueTrigger' in",
                        "You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in local.settings.json or User Secrets."
                    },
                    Test = async (workingDir, p) =>
                    {
                        using (var client = new HttpClient() { BaseAddress = new Uri("http://localhost:7071/") })
                        {
                            (await WaitUntilReady(client)).Should().BeTrue(because: _serverNotReady);
                            var response = await client.GetAsync("/api/http1?name=Test");
                            response.StatusCode.Should().Be(HttpStatusCode.OK);
                            p.Kill();
                        }
                    }
                }
            }, _output);
        }

        private async Task<bool> WaitUntilReady(HttpClient client)
        {
            for (var limit = 0; limit < 10; limit++)
            {
                try
                {
                    var response = await client.GetAsync("/admin/host/ping");
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    await Task.Delay(1000);
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
            return false;
        }

        private void SetUserSecrets(string workingDir, Dictionary<string, string> userSecrets)
        {
            // init and set user secrets
            string procOutput;
            Process proc = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    FileName = "cmd.exe",
                    Arguments = "/C dotnet user-secrets init",
                    WorkingDirectory = workingDir
                }
            };
            proc.Start();
            procOutput = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            _output.WriteLine(procOutput);

            foreach (KeyValuePair<string, string> pair in userSecrets)
            {
                proc.StartInfo.Arguments = $"/C dotnet user-secrets set \"{pair.Key}\" \"{pair.Value}\"";
                proc.Start();
                procOutput = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                _output.WriteLine(procOutput);
            }
        }
    }
}