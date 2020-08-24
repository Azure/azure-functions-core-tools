using System;
using System.IO;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Azure.Functions.Cli.Helpers;
using Xunit;

namespace Azure.Functions.Cli.Tests
{
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