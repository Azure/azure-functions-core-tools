// Requires Function App resource

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Xunit;
using static Azure.Functions.Cli.Actions.AzureActions.PublishFunctionAppAction;

namespace Azure.Functions.Cli.Tests
{
    public class PublishActionTests
    {
        private readonly TestAzureHelperService _helperService = new TestAzureHelperService();

        [Theory]
        [InlineData(null, "6.0")]
        [InlineData("something", "6.0")]
        [InlineData("6.0", "6.0")]
        [InlineData("7.0", "7.0")]
        [InlineData("9.0", "9.0")]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Consumption_Updated(string initialLinuxFxVersion, string expectedNetFrameworkVersion)
        {
            var site = new Site("test")
            {
                Kind = "linux",
                Sku = "dynamic",
                LinuxFxVersion = initialLinuxFxVersion
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, expectedNetFrameworkVersion, false, _helperService);

            // update it to empty
            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal($"DOTNET-ISOLATED|{expectedNetFrameworkVersion}", setting.Value);
        }

        [Theory]
        [InlineData("v6.0")]
        [InlineData("6.0")]
        [InlineData("6.0.1")]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Dedicated(string specifiedVersion)
        {
            var site = new Site("test")
            {
                Kind = "linux"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, specifiedVersion, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal("DOTNET-ISOLATED|6.0", setting.Value);
        }

        [Theory]
        [InlineData("v6.0")]
        [InlineData("6.0")]
        [InlineData("6.0.1")]
        public async Task NetFrameworkVersion_DotnetIsolated_Windows(string specifiedVersion)
        {
            var site = new Site("test");

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, specifiedVersion, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v6.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Null()
        {
            // If not specified, assume 8.0
            var site = new Site("test")
            {
                Kind = "linux"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal("DOTNET-ISOLATED|8.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Windows_Null()
        {
            // If not specified, assume 8.0
            var site = new Site("test");

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v8.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_Dotnet_Windows_Null()
        {
            var site = new Site("test")
            {
                NetFrameworkVersion = "v4.0"
            };

            // If not supported specified, assume 8.0
            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.Dotnet, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v8.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_Dotnet_Windows_NoOp()
        {
            var site = new Site("test")
            {
                NetFrameworkVersion = "v8.0"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.Dotnet, null, false, _helperService);

            // Should be a no-op as site is already v6.0
            Assert.Null(_helperService.UpdatedSettings);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("6.0.a.b")]
        public async Task NetFrameworkVersion_Invalid(string specifiedVersion)
        {
            var site = new Site("test");

            var exception = await Assert.ThrowsAsync<CliException>(() =>
                PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.DotnetIsolated, specifiedVersion, false, _helperService));

            Assert.StartsWith($"The dotnet-version value of '{specifiedVersion}' is invalid.", exception.Message);
        }

        [Theory]
        [InlineData("dotnet-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("c#-isolated", WorkerRuntime.DotnetIsolated)]
        [InlineData("csharp", WorkerRuntime.Dotnet)]
        [InlineData("typescript", WorkerRuntime.Node)]
        public void NormalizeWorkerRuntime_ReturnsExpectedWorkerRuntime(string input, WorkerRuntime expected)
        {

            var result = WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(input);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("invalid-runtime")]
        [InlineData("unknown")]
        [InlineData("dotnet-isol")]
        [InlineData("c-sharp")]
        [InlineData("")]
        [InlineData(null)]
        public void NormalizeWorkerRuntime_InvalidInput(string inputString)
        {
            if (string.IsNullOrWhiteSpace(inputString))
            {
                var exception = Assert.Throws<ArgumentNullException>(() => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(inputString));
                Assert.StartsWith($"Worker runtime cannot be null or empty.", exception.Message);
                Assert.Equal("workerRuntime", exception.ParamName);
            }
            else
            {
                var exception = Assert.Throws<ArgumentException>(() => WorkerRuntimeLanguageHelper.NormalizeWorkerRuntime(inputString));
                Assert.Equal($"Worker runtime '{inputString}' is not a valid option. Options are {WorkerRuntimeLanguageHelper.AvailableWorkersRuntimeString}", exception.Message);
            }
        }

        [Fact]
        public void ValidateFunctionAppPublish_ThrowException_WhenWorkerRuntimeIsNone()
        {
            GlobalCoreToolsSettings.SetWorkerRuntime(WorkerRuntime.None);

            var ex = Assert.Throws<CliException>(() => GlobalCoreToolsSettings.CurrentWorkerRuntime);
            Assert.Equal($"Worker runtime cannot be '{WorkerRuntime.None}'. Please set a valid runtime.", ex.Message);
        }

        private class TestAzureHelperService : AzureHelperService
        {
            public Dictionary<string, string> UpdatedSettings { get; private set; }

            public TestAzureHelperService()
                : base(null, null)
            {
            }

            public override Task<HttpResult<string, string>> UpdateWebSettings(Site functionApp, Dictionary<string, string> updatedSettings)
            {
                UpdatedSettings = updatedSettings;
                return Task.FromResult(new HttpResult<string, string>(string.Empty));
            }
        }
    }
}
