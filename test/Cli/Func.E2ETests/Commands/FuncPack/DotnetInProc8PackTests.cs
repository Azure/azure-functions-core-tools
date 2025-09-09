// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProc8PackTests : BaseE2ETests
    {
        public DotnetInProc8PackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string Dotnet8ProjectPath => Path.Combine(TestProjectDirectory, "TestNet8InProcProject");

        [Fact]
        public void Pack_Dotnet8InProc_WorksAsExpected()
        {
            var testName = nameof(Pack_Dotnet8InProc_WorksAsExpected);

            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore..."
            };

            BasePackTests.TestBasicPackFunctionality(
                Dotnet8ProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.pdb"),
                    Path.Combine("Dotnet8InProc", "function.json")
                },
                logsToValidate);
        }

        [Fact]
        public async Task Pack_Dotnet8InProc_CustomOutput_NoBuild()
        {
            var testName = nameof(Pack_Dotnet8InProc_CustomOutput_NoBuild);

            await BasePackTests.TestDotnetNoBuildCustomOutputPackFunctionality(
                Dotnet8ProjectPath,
                testName,
                FuncPath,
                Log,
                WorkingDirectory,
                new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.pdb"),
                    Path.Combine("Dotnet8InProc", "function.json")
                });
        }

        [Fact]
        public void Pack_Dotnet8InProc_WithRelativePathArgument_Works()
        {
            var testName = nameof(Pack_Dotnet8InProc_WithRelativePathArgument_Works);
            var projectName = "TestNet8InProcProject";
            BasePackTests.TestPackWithPathArgument(
                funcInvocationWorkingDir: TestProjectDirectory,
                projectAbsoluteDir: Path.Combine(TestProjectDirectory, projectName),
                pathArgumentToPass: $"./{projectName}",
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.pdb"),
                    Path.Combine("Dotnet8InProc", "function.json")
                });
        }

        [Fact]
        public void Pack_Dotnet8InProc_WithAbsolutePathArgument_Works()
        {
            var testName = nameof(Pack_Dotnet8InProc_WithAbsolutePathArgument_Works);
            var projectAbs = Dotnet8ProjectPath;
            BasePackTests.TestPackWithPathArgument(
                funcInvocationWorkingDir: WorkingDirectory,
                projectAbsoluteDir: projectAbs,
                pathArgumentToPass: projectAbs,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json",
                    Path.Combine("bin", "extensions.json"),
                    Path.Combine("bin", "function.deps.json"),
                    Path.Combine("bin", "Microsoft.Azure.WebJobs.Host.Storage.dll"),
                    Path.Combine("bin", "Microsoft.WindowsAzure.Storage.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.dll"),
                    Path.Combine("bin", "TestNet8InProcProject.pdb"),
                    Path.Combine("Dotnet8InProc", "function.json")
                });
        }

        [Fact]
        public void Pack_DotnetInProc8_WithDirectoryBuildProps_Works()
        {
            var testName = nameof(Pack_DotnetInProc8_WithDirectoryBuildProps_Works);
            var projectAbs = Dotnet8ProjectPath;
            var logsToValidate = new[]
            {
                "Building .NET project...",
                "Determining projects to restore...",
                Path.Combine(projectAbs, "customArtifacts")
            };

            BasePackTests.TestPackWithDirectoryBuildProps(
                projectAbsoluteDir: projectAbs,
                noBuild: false,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json"
                },
                logsToValidate: logsToValidate);
        }

        [Fact]
        public void Pack_DotnetInProc8_WithDirectoryBuildProps_NoBuild_Works()
        {
            var testName = nameof(Pack_DotnetInProc8_WithDirectoryBuildProps_NoBuild_Works);
            var projectAbs = Dotnet8ProjectPath;
            var logsToValidate = new[]
            {
                "Found ArtifactsPath within Directory.Build.props. Using as build output directory.",
                "Skipping build event for functions project (--no-build)."
            };
            BasePackTests.TestPackWithDirectoryBuildProps(
                projectAbsoluteDir: projectAbs,
                noBuild: true,
                testName: testName,
                funcPath: FuncPath,
                log: Log,
                filesToValidate: new[]
                {
                    "host.json"
                },
                logsToValidate: logsToValidate);
        }
    }
}
