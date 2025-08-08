// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using Colors.Net;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class ExtensionBundleHelperTests
    {
        [Fact]
        public void GetBundleDownloadPath_ReturnCorrectPath()
        {
            var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath("BundleId");
            var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure-functions-core-tools", "Functions", "ExtensionBundles", "BundleId");
            Assert.Equal(expectedPath, downloadPath);
        }
    }
}
