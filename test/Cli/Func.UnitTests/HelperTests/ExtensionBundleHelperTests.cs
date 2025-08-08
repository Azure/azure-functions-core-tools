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

            var output = new StringBuilder();
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;
            ColoredConsole.Error = console;

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await ExtensionBundleHelper.GetExtensionBundle();

            // Assert
            Assert.Contains("Extension Bundle already exists", output.ToString());
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = _originalFileSystem;
        }
    }
}
