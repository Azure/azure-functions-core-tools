// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class GoPackResolveOutputPathTests : IDisposable
    {
        private readonly string _tempDirectory;

        public GoPackResolveOutputPathTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "func-pack-go-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        // Test subclass exposes the protected ResolveOutputPath override defined on
        // GoPackSubcommandAction so the Go-specific existing-file guardrail can be
        // exercised without invoking the full pack pipeline.
        private sealed class TestGoPackSubcommandAction : GoPackSubcommandAction
        {
            public string InvokeResolveOutputPath(string functionAppRoot, string outputPath)
                => ResolveOutputPath(functionAppRoot, outputPath);
        }

        [Fact]
        public void ResolveOutputPath_NoOutputProvided_UsesFunctionAppRootName()
        {
            var action = new TestGoPackSubcommandAction();
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");

            var result = action.InvokeResolveOutputPath(functionAppRoot, outputPath: null);

            result.Should().Be(Path.Combine(Environment.CurrentDirectory, "myapp.zip"));
        }

        [Fact]
        public void ResolveOutputPath_OutputIsDirectory_PlacesZipInside()
        {
            var action = new TestGoPackSubcommandAction();
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");
            var outputPath = Path.Combine(_tempDirectory, "pkg");

            var result = action.InvokeResolveOutputPath(functionAppRoot, outputPath);

            result.Should().Be(Path.Combine(outputPath, "myapp.zip"));
            Directory.Exists(outputPath).Should().BeTrue();
        }

        [Fact]
        public void ResolveOutputPath_OutputIsExistingFile_ThrowsClearError()
        {
            var action = new TestGoPackSubcommandAction();
            var functionAppRoot = Path.Combine(_tempDirectory, "myapp");
            var existingFile = Path.Combine(_tempDirectory, "already-a-file");
            File.WriteAllText(existingFile, "marker");

            Action act = () => action.InvokeResolveOutputPath(functionAppRoot, existingFile);

            act.Should().Throw<CliException>()
                .WithMessage("*existing file*")
                .WithMessage("*directory*");
        }
    }
}
