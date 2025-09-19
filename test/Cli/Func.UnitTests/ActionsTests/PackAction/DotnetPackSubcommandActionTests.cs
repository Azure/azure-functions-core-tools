// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class DotnetPackSubcommandActionTests : System.IDisposable
    {
        private readonly string _tempDirectory;

        public DotnetPackSubcommandActionTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void ValidateDotnetFolderStructure_ValidStructure_ReturnsTrue()
        {
            File.WriteAllText(Path.Combine(_tempDirectory, "host.json"), "{}");
            File.WriteAllText(Path.Combine(_tempDirectory, "functions.metadata"), "{}");
            Directory.CreateDirectory(Path.Combine(_tempDirectory, ".azurefunctions"));

            var result = DotnetPackSubcommandAction.ValidateDotnetIsolatedFolderStructure(_tempDirectory, out string errorMessage);

            Assert.True(result);
            Assert.Empty(errorMessage);
        }
    }
}
