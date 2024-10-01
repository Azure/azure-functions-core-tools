using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Tests;
using Xunit;

namespace Azure.Functions.Cli.PublishHelperTests
{
    [Trait(TestTraits.Category, TestTraits.UnitTest)]
    public class PublishHelperTests
    {
        [Theory]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/node", false)]
        [InlineData("DOCKER|customimage", true)]
        [InlineData("PYTHON|3.6", false)]
        [InlineData("DOTNET", false)]
        [InlineData("", false)]
        public void IsLinuxFxVersionUsingCustomImageTest(string linuxFxVersion, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionUsingCustomImage(linuxFxVersion));
        }

        [Theory]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/dotnet", WorkerRuntime.dotnet, true)]
        [InlineData("DOCKER|mcr.microsoft.com/azure-functions/node", WorkerRuntime.dotnet, false)]
        [InlineData("DOCKER|customimage", WorkerRuntime.dotnet, false)]
        [InlineData("PYTHON|3.7", WorkerRuntime.python, true)]
        [InlineData("PYTHON|3.7", WorkerRuntime.node, false)]
        [InlineData("", WorkerRuntime.dotnet, true)]
        public void IsLinuxFxVersionRuntimeMatchedTest(string linuxFxVersion, WorkerRuntime runtime, bool expected)
        {
            Assert.Equal(expected, PublishHelper.IsLinuxFxVersionRuntimeMatched(linuxFxVersion, runtime));
        }
    }
}
