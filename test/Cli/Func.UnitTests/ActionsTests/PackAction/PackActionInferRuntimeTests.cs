// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Actions.LocalActions.PackAction;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests.PackAction
{
    public class PackActionInferRuntimeTests : IDisposable
    {
        private readonly string _dir;

        public PackActionInferRuntimeTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }

        // --- .csproj signals ---

        [Fact]
        public void InferRuntime_CsprojWithIsolatedWorkerReference_ReturnsDotnetIsolated()
        {
            File.WriteAllText(Path.Combine(_dir, "MyApp.csproj"),
                "<Project><ItemGroup><PackageReference Include=\"Microsoft.Azure.Functions.Worker\" Version=\"1.0\" /></ItemGroup></Project>");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.DotnetIsolated);
        }

        [Fact]
        public void InferRuntime_CsprojWithoutIsolatedReference_ReturnsDotnet()
        {
            File.WriteAllText(Path.Combine(_dir, "MyApp.csproj"),
                "<Project><ItemGroup><PackageReference Include=\"Microsoft.NET.Sdk.Functions\" Version=\"4.0\" /></ItemGroup></Project>");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Dotnet);
        }

        // --- Python signals ---

        [Fact]
        public void InferRuntime_RequirementsTxtPresent_ReturnsPython()
        {
            File.WriteAllText(Path.Combine(_dir, "requirements.txt"), "azure-functions");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Python);
        }

        [Fact]
        public void InferRuntime_FunctionAppPyPresent_ReturnsPython()
        {
            File.WriteAllText(Path.Combine(_dir, "function_app.py"), "import azure.functions as func");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Python);
        }

        // --- Node signal ---

        [Fact]
        public void InferRuntime_PackageJsonPresent_ReturnsNode()
        {
            File.WriteAllText(Path.Combine(_dir, "package.json"), "{}");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Node);
        }

        // --- PowerShell signals ---

        [Fact]
        public void InferRuntime_ProfilePs1Present_ReturnsPowershell()
        {
            File.WriteAllText(Path.Combine(_dir, "profile.ps1"), "# profile");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Powershell);
        }

        [Fact]
        public void InferRuntime_PsdFilePresent_ReturnsPowershell()
        {
            File.WriteAllText(Path.Combine(_dir, "MyModule.psd1"), "# module");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Powershell);
        }

        // --- Build-output signals (--no-build scenarios) ---

        [Fact]
        public void InferRuntime_FunctionsMetadataPresent_ReturnsDotnetIsolated()
        {
            File.WriteAllText(Path.Combine(_dir, "functions.metadata"), "[]");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.DotnetIsolated);
        }

        [Fact]
        public void InferRuntime_DllFilesPresent_ReturnsDotnet()
        {
            File.WriteAllText(Path.Combine(_dir, "MyApp.dll"), string.Empty);

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Dotnet);
        }

        // --- No signal ---

        [Fact]
        public void InferRuntime_EmptyDirectory_ReturnsNone()
        {
            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.None);
        }

        // --- Priority: .csproj beats other signals ---

        [Fact]
        public void InferRuntime_CsprojAndRequirementsTxtPresent_ReturnsDotnet()
        {
            File.WriteAllText(Path.Combine(_dir, "MyApp.csproj"),
                "<Project><ItemGroup></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(_dir, "requirements.txt"), "azure-functions");

            var result = PackAction.InferWorkerRuntimeFromProjectFiles(_dir);

            result.Should().Be(WorkerRuntime.Dotnet);
        }
    }
}
