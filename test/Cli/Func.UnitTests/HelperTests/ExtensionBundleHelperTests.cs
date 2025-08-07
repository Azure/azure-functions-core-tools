// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ExtensionBundle;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.HelperTests
{
    public class ExtensionBundleHelperTests : IDisposable
    {
        private readonly IFileSystem _originalFileSystem;

        public ExtensionBundleHelperTests()
        {
            _originalFileSystem = FileSystemHelpers.Instance;
        }

        [Fact]
        public void GetBundleDownloadPath_ReturnCorrectPath()
        {
            var downloadPath = ExtensionBundleHelper.GetBundleDownloadPath("BundleId");
            var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure-functions-core-tools", "Functions", "ExtensionBundles", "BundleId");
            Assert.Equal(expectedPath, downloadPath);
        }

        [Fact]
        public async Task GetExtensionBundle_SkipsDownload_WhenFilesExist()
        {
            // Arrange
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.Directory.Exists(Arg.Any<string>()).Returns(true);
            fileSystem.Directory.GetFiles(Arg.Any<string>(), "*", SearchOption.AllDirectories).Returns(new[] { "existing.dll" });

            var originalOut = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await ExtensionBundleHelper.GetExtensionBundle();

            // Restore the real console
            Console.SetOut(originalOut);

            // Assert
            var output = sw.ToString();
            Assert.Contains("Extension Bundle already exists", output);
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = _originalFileSystem;
        }
    }
}
