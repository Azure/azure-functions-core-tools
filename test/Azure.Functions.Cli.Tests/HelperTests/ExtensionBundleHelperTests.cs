using System;
using System.IO;
using Azure.Functions.Cli.ExtensionBundle;
using Xunit;

namespace Azure.Functions.Cli.Tests.HelperTests
{
    [Trait(TestTraits.Category, TestTraits.UnitTest)]
    public class ExtensionBundleHelperTests
    {
        [Fact]
        public void VerifyGetBundleDownloadPathReturnCorrectPath()
        {
            var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath("BundleId");
            var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure-functions-core-tools", "Functions", "ExtensionBundles", "BundleId");
            Assert.Equal(expectedPath, downloadPath);
        }
    }
}