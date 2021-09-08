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
        TestAzureHelperService _helperService = new TestAzureHelperService();

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Consumption_AlreadyEmpty()
        {
            var site = new Site("test")
            {
                Kind = "linux",
                Sku = "dynamic",
                LinuxFxVersion = null
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, "6.0", false, _helperService);

            // no-op if already null or empty
            Assert.Null(_helperService.UpdatedSettings);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Consumption_Updated()
        {
            var site = new Site("test")
            {
                Kind = "linux",
                Sku = "dynamic",
                LinuxFxVersion = "something"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, "6.0", false, _helperService);

            // update it to empty
            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal(string.Empty, setting.Value);
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

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, specifiedVersion, false, _helperService);

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

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, specifiedVersion, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v6.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Null()
        {
            // If not specified, assume 5.0
            var site = new Site("test")
            {
                Kind = "linux"
            };

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal("DOTNET-ISOLATED|5.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Windows_Null()
        {
            // If not specified, assume 5.0
            var site = new Site("test");

            await PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, null, false, _helperService);

            var setting = _helperService.UpdatedSettings.Single();
            Assert.Equal(Constants.DotnetFrameworkVersion, setting.Key);
            Assert.Equal("v5.0", setting.Value);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("6.0.a.b")]
        public async Task NetFrameworkVersion_Invalid(string specifiedVersion)
        {
            var site = new Site("test");

            var exception = await Assert.ThrowsAsync<CliException>(() =>
                PublishFunctionAppAction.UpdateFrameworkVersions(site, WorkerRuntime.dotnetIsolated, specifiedVersion, false, _helperService));

            Assert.StartsWith($"The dotnet-version value of '{specifiedVersion}' is invalid.", exception.Message);
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
