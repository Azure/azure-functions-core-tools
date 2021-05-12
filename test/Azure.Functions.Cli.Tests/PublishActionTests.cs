using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.AzureActions;
using Azure.Functions.Cli.Arm.Models;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
    public class PublishActionTests
    {
        private Dictionary<string, string> _settings;

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Linux_Consumption_AlreadyEmpty()
        {
            var site = new Site("test")
            {
                Kind = "linux",
                Sku = "dynamic",
                LinuxFxVersion = null
            };

            await PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, "6.0", UpdateWebSettings);

            // no-op if already null or empty
            Assert.Null(_settings);
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

            await PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, "6.0", UpdateWebSettings);

            // update it to empty
            var setting = _settings.Single();
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

            await PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, specifiedVersion, UpdateWebSettings);

            var setting = _settings.Single();
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

            await PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, specifiedVersion, UpdateWebSettings);

            var setting = _settings.Single();
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

            await PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, null, UpdateWebSettings);

            var setting = _settings.Single();
            Assert.Equal(Constants.LinuxFxVersion, setting.Key);
            Assert.Equal("DOTNET-ISOLATED|5.0", setting.Value);
        }

        [Fact]
        public async Task NetFrameworkVersion_DotnetIsolated_Windows_Null()
        {
            // If not specified, assume 5.0
            var site = new Site("test");

            await PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, null, UpdateWebSettings);

            var setting = _settings.Single();
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
                PublishFunctionAppAction.UpdateDotNetIsolatedFrameworkVersion(site, specifiedVersion, UpdateWebSettings));

            Assert.StartsWith($"The dotnet-framework-version value of '{specifiedVersion}' is invalid.", exception.Message);
        }

        private Task<HttpResult<string, string>> UpdateWebSettings(Dictionary<string, string> settings)
        {
            _settings = settings;
            return Task.FromResult(new HttpResult<string, string>(string.Empty));
        }
    }
}
